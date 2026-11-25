using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PokeArena.Api.Data;
using PokeArena.Api.Modules.Matchmaking.Application;
using PokeArena.Api.Modules.Matchmaking.Domain;

namespace PokeArena.Api.Modules.Matchmaking.Infrastructure;

// ADAPTADOR DE SALIDA: LA FILA DE BÚSQUEDA EN POSTGRESQL (módulo Emparejamiento).
//
// Es el único lugar del módulo que sabe que la fila vive en la tabla match_searches.
// Lo compartido entre instancias está en la base de datos (D-01, D-05, ADR-007):
// - Una sola búsqueda abierta por jugador: candado por jugador (pg_advisory_xact_lock) más el
//   índice único parcial IX_match_searches_PlayerId (solo filas Waiting o Pairing).
// - Emparejar es atómico: las dos filas se bloquean con FOR UPDATE, siempre en orden de Id, y
//   se vuelve a comprobar que siguen en espera. Si otra instancia emparejó antes a alguno de los
//   dos, esta lo ve y no los empareja. El orden fijo evita bloqueos mutuos entre instancias.
// - Los vencimientos comparan marcas de tiempo guardadas en la tabla, así que cualquier
//   instancia da la misma respuesta (RNF-INT-08).
public sealed class EfMatchSearchRepository(PokeArenaDbContext db) : IMatchSearchRepository
{
    public Task<MatchSearch?> FindLatestAsync(Guid playerId, CancellationToken cancellationToken) =>
        db.MatchSearches.AsNoTracking()
            .Where(search => search.PlayerId == playerId)
            .OrderByDescending(search => search.CreatedAt)
            .ThenByDescending(search => search.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<MatchSearch?> FindAsync(Guid searchId, CancellationToken cancellationToken) =>
        db.MatchSearches.AsNoTracking()
            .SingleOrDefaultAsync(search => search.Id == searchId, cancellationToken);

    public async Task<IPlayerSearchScope> BeginPlayerScopeAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Mismo candado que Crear Deck, con otro prefijo: las solicitudes de búsqueda de un
            // mismo jugador se atienden de una en una, en cualquier instancia.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext({0}))",
                new object[] { "match-search:" + playerId },
                cancellationToken);
            return new PlayerScope(db, transaction, playerId);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IPairingScope> BeginPairingScopeAsync(Guid searchId, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Sin rastrear: LockAsync debe traer los valores de la base, no estos.
            var snapshot = await db.MatchSearches.AsNoTracking()
                .SingleOrDefaultAsync(search => search.Id == searchId, cancellationToken);
            return new PairingScope(db, transaction, snapshot);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task CompletePairingAsync(
        Guid pairingId,
        Guid matchId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Las dos búsquedas del emparejamiento en una sola sentencia: las dos o ninguna.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE match_searches
               SET ""Status"" = 'MatchFound', ""MatchId"" = {matchId}, ""ClosedAt"" = {now}, ""Notice"" = NULL
               WHERE ""PairingId"" = {pairingId} AND ""Status"" = 'Pairing'",
            cancellationToken);
    }

    public async Task ReturnPairingToWaitingAsync(Guid pairingId, string reason, CancellationToken cancellationToken)
    {
        var notice = reason.Length > MaxNoticeLength ? reason[..MaxNoticeLength] : reason;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE match_searches
               SET ""Status"" = 'Waiting', ""PairingId"" = NULL, ""PairedAt"" = NULL, ""Notice"" = {notice}
               WHERE ""PairingId"" = {pairingId} AND ""Status"" = 'Pairing'",
            cancellationToken);
    }

    // Mismo largo que la columna Notice (PokeArenaDbContext).
    public const int MaxNoticeLength = 300;

    private sealed class PlayerScope(
        PokeArenaDbContext db,
        IDbContextTransaction transaction,
        Guid playerId) : IPlayerSearchScope
    {
        public async Task<MatchSearch?> FindOpenAsync(CancellationToken cancellationToken)
        {
            var rows = await db.MatchSearches
                .FromSqlInterpolated($@"SELECT * FROM match_searches
                    WHERE ""PlayerId"" = {playerId} AND ""Status"" IN ('Waiting', 'Pairing')
                    FOR UPDATE")
                .ToListAsync(cancellationToken);
            return rows.SingleOrDefault();
        }

        public void Add(MatchSearch search) => db.MatchSearches.Add(search);

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new OpenSearchAlreadyExistsException(playerId, exception);
            }
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            // Las búsquedas leídas aquí no deben seguir rastreadas: la siguiente lectura debe
            // traer los valores de la base, no los de memoria.
            db.ChangeTracker.Clear();
        }
    }

    private sealed class PairingScope(
        PokeArenaDbContext db,
        IDbContextTransaction transaction,
        MatchSearch? snapshot) : IPairingScope
    {
        public MatchSearch? Snapshot { get; } = snapshot;

        public async Task<MatchSearch?> FindCandidateAsync(
            MatchSearch mine,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            // Los valores salen de MatchmakingRules; la fórmula del rango del rival es la misma
            // que MatchmakingRules.EloRangeFor. Se acepta la diferencia si cabe en el rango del
            // que lleva más tiempo esperando. Primero el Elo más cercano y, a igual Elo, quien
            // lleva más tiempo esperando.
            var activeSince = now - MatchmakingRules.InactivityTimeout;
            var myRange = MatchmakingRules.EloRangeFor(now - mine.CreatedAt);
            var stepSeconds = (int)MatchmakingRules.EloRangeStep.TotalSeconds;
            var initialRange = MatchmakingRules.InitialEloRange;
            var increment = MatchmakingRules.EloRangeIncrement;
            var maxRange = MatchmakingRules.MaxEloRange;

            var rows = await db.MatchSearches
                .FromSqlInterpolated($@"SELECT * FROM match_searches
                    WHERE ""Status"" = 'Waiting'
                      AND ""Context"" = {mine.Context}
                      AND ""PlayerId"" <> {mine.PlayerId}
                      AND ""LastSeenAt"" >= {activeSince}
                      AND abs(""Elo"" - {mine.Elo}) <= GREATEST({myRange}, LEAST({maxRange},
                            {initialRange} + floor(GREATEST(extract(epoch FROM ({now} - ""CreatedAt"")), 0) / {stepSeconds})::integer * {increment}))
                    ORDER BY abs(""Elo"" - {mine.Elo}), ""CreatedAt"", ""Id""
                    LIMIT 1")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            return rows.SingleOrDefault();
        }

        public async Task<IReadOnlyList<MatchSearch>> LockAsync(
            IReadOnlyCollection<Guid> searchIds,
            CancellationToken cancellationToken)
        {
            // PostgreSQL bloquea las filas en el orden del ORDER BY: siempre por Id.
            var ids = searchIds.Distinct().ToArray();
            return await db.MatchSearches
                .FromSqlInterpolated($@"SELECT * FROM match_searches
                    WHERE ""Id"" = ANY({ids})
                    ORDER BY ""Id""
                    FOR UPDATE")
                .ToListAsync(cancellationToken);
        }

        public Task<MatchSearch?> FindPartnerAsync(MatchSearch mine, CancellationToken cancellationToken) =>
            db.MatchSearches.AsNoTracking()
                .FirstOrDefaultAsync(
                    other => other.PairingId == mine.PairingId
                             && other.Id != mine.Id
                             && other.Status == MatchSearchStatus.Pairing,
                    cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
        }
    }
}
