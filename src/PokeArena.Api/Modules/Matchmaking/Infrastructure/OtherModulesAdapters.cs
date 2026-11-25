using Microsoft.EntityFrameworkCore;
using PokeArena.Api.Data;
using PokeArena.Api.Modules.Matches.Application;
using PokeArena.Api.Modules.Matchmaking.Application;

namespace PokeArena.Api.Modules.Matchmaking.Infrastructure;

// ADAPTADORES HACIA OTROS MÓDULOS (módulo Emparejamiento, capa de infraestructura).
//
// Cada clase implementa un puerto de MatchmakingPorts.cs. Las lecturas usan el mismo
// PokeArenaDbContext que el resto de la API (un solo despliegue y una sola base, ADR-001), y
// la creación de la partida pasa por el caso de uso público del módulo Partida.

// Módulo Colección: el deck del jugador y cuántas de sus cartas siguen en su colección.
public sealed class CollectionSearchDeckLookup(PokeArenaDbContext db) : ISearchDeckLookup
{
    public async Task<SearchDeck?> FindAsync(Guid playerId, Guid deckId, CancellationToken cancellationToken)
    {
        // El filtro por PlayerId es lo que impide usar el deck de otro jugador.
        var deck = await db.Decks.AsNoTracking()
            .Include(item => item.Cards)
            .SingleOrDefaultAsync(item => item.Id == deckId && item.PlayerId == playerId, cancellationToken);
        if (deck is null)
            return null;

        var cardIds = deck.Cards.Select(card => card.CardId).ToList();
        var distinctIds = cardIds.Distinct().ToArray();
        var owned = await db.CardCopies.AsNoTracking()
            .Where(copy => copy.PlayerId == playerId && distinctIds.Contains(copy.CardId))
            .Select(copy => copy.CardId)
            .Distinct()
            .CountAsync(cancellationToken);

        return new SearchDeck(deck.Id, deck.Name, cardIds, owned);
    }
}

// Módulo Colección: saldo de cubos guardado en cube_balances.
public sealed class CollectionCubeBalanceLookup(PokeArenaDbContext db) : ICubeBalanceLookup
{
    public async Task<int> GetBalanceAsync(Guid playerId, CancellationToken cancellationToken) =>
        await db.CubeBalances.AsNoTracking()
            .Where(balance => balance.PlayerId == playerId)
            .Select(balance => (int?)balance.Balance)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
}

// Módulo Identidad: el Elo está en la cuenta del jugador (empieza en 0). Cuando exista el
// módulo Clasificación, solo cambia este adaptador.
public sealed class IdentityPlayerEloLookup(PokeArenaDbContext db) : IPlayerEloLookup
{
    public Task<int?> FindEloAsync(Guid playerId, CancellationToken cancellationToken) =>
        db.PlayerAccounts.AsNoTracking()
            .Where(account => account.Id == playerId)
            .Select(account => (int?)account.Elo)
            .SingleOrDefaultAsync(cancellationToken);
}

// Módulo Partida: una partida es activa mientras no esté terminada. Hoy las partidas se crean
// en estado "Created" y todavía no existe la historia que las termina (#135).
public sealed class MatchesActiveMatchLookup(PokeArenaDbContext db) : IActiveMatchLookup
{
    public const string FinishedStatus = "Finished";

    public Task<Guid?> FindActiveMatchIdAsync(Guid playerId, CancellationToken cancellationToken) =>
        db.Matches.AsNoTracking()
            .Where(match => (match.ChallengerPlayerId == playerId || match.OpponentPlayerId == playerId)
                            && match.Status != FinishedStatus)
            .OrderByDescending(match => match.CreatedAt)
            .Select(match => (Guid?)match.Id)
            .FirstOrDefaultAsync(cancellationToken);
}

// Módulo Partida: crea la partida con su caso de uso público, StartMatchUseCase.
//
// Llave de idempotencia: "matchmaking:" + PairingId. Si el emparejamiento se reintenta, se
// recupera la misma partida en lugar de crear otra.
//
// Ubicación: StartMatchUseCase pide hoy UNA ubicación (ADR-006). Mientras la US #129 no elija
// las 3 ubicaciones al azar, aquí se elige una de las disponibles a partir del PairingId, para
// que un reintento elija la misma. Ningún dato del cliente influye en la elección.
public sealed class MatchesPairedMatchStarter(PokeArenaDbContext db, StartMatchUseCase startMatch)
    : IPairedMatchStarter
{
    public async Task<PairedMatchOutcome> StartAsync(PairedMatchRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = "matchmaking:" + request.PairingId.ToString("N");

        // ¿Un intento anterior ya creó la partida? Entonces es esa.
        var existingId = await db.Matches.AsNoTracking()
            .Where(match => match.IdempotencyKey == idempotencyKey)
            .Select(match => (Guid?)match.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingId is { } id)
            return PairedMatchOutcome.Started(id);

        var locationIds = await db.Locations.AsNoTracking()
            .Where(location => location.IsAvailable)
            .OrderBy(location => location.Id)
            .Select(location => location.Id)
            .ToListAsync(cancellationToken);
        if (locationIds.Count == 0)
            return PairedMatchOutcome.Failed(
                "Se encontró rival, pero no hay ubicaciones disponibles para crear la partida. Sigues en la búsqueda.");

        var index = (int)(BitConverter.ToUInt32(request.PairingId.ToByteArray(), 0) % (uint)locationIds.Count);

        var result = await startMatch.ExecuteAsync(
            new StartMatchCommand(
                idempotencyKey,
                request.ChallengerId,
                request.OpponentId,
                request.ChallengerCardIds,
                request.OpponentCardIds,
                locationIds[index]),
            cancellationToken);

        return result.Error is { } error
            ? PairedMatchOutcome.Failed($"Se encontró rival, pero la partida no se pudo crear: {error.Message} Sigues en la búsqueda.")
            : PairedMatchOutcome.Started(result.Match!.Id);
    }
}
