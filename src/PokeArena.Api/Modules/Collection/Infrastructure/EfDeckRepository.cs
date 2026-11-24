using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PokeArena.Api.Data;
using PokeArena.Api.Modules.Collection.Application;
using PokeArena.Api.Modules.Collection.Domain;

namespace PokeArena.Api.Modules.Collection.Infrastructure;

// ADAPTADOR DE SALIDA: DECKS EN POSTGRESQL (módulo Colección).
//
// Implementa el puerto IDeckRepository con Entity Framework. Es el único lugar del módulo
// que sabe que los decks viven en las tablas decks y deck_cards.
//
// Cómo se evitan duplicados: DOS mecanismos que se complementan, igual que en la entrega
// inicial.
// 1. CANDADO POR JUGADOR (pg_advisory_xact_lock): las creaciones de un mismo jugador se
//    atienden de una en una. El candado vive en la base de datos, no en la memoria de la
//    API, así que funciona con varias instancias. Se suelta solo al terminar la transacción.
// 2. ÍNDICES ÚNICOS de decks: (PlayerId, IdempotencyKey) y (PlayerId, Name). Son la última
//    barrera si algún camino de código se saltara el candado.
public sealed class EfDeckRepository(PokeArenaDbContext db) : IDeckRepository
{
    public async Task<IDeckCreationScope> BeginCreationAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // hashtext convierte el texto en el número que pide el candado. Si dos textos
            // distintos dieran el mismo número, solo esperarían de más: nunca se mezclan datos.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext({0}))",
                new object[] { "deck-creation:" + playerId },
                cancellationToken);
            return new CreationScope(db, transaction, playerId);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    // Se pregunta por los ids pedidos (como máximo 12), no por toda la colección. El índice
    // (PlayerId, CardId) de card_copies hace que la consulta sea barata aunque el jugador
    // tenga muchas copias.
    public async Task<IReadOnlyList<int>> GetOwnedCardIdsAsync(
        Guid playerId,
        IReadOnlyCollection<int> cardIds,
        CancellationToken cancellationToken)
    {
        var ids = cardIds.ToArray();
        return await db.CardCopies.AsNoTracking()
            .Where(copy => copy.PlayerId == playerId && ids.Contains(copy.CardId))
            .Select(copy => copy.CardId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public Task<Deck?> FindByIdempotencyKeyAsync(
        Guid playerId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        db.Decks.AsNoTracking()
            .Include(deck => deck.Cards)
            .SingleOrDefaultAsync(
                deck => deck.PlayerId == playerId && deck.IdempotencyKey == idempotencyKey,
                cancellationToken);

    public async Task<IReadOnlyList<Deck>> ListAsync(Guid playerId, CancellationToken cancellationToken) =>
        await db.Decks.AsNoTracking()
            .Include(deck => deck.Cards)
            .Where(deck => deck.PlayerId == playerId)
            .OrderBy(deck => deck.CreatedAt)
            .ThenBy(deck => deck.Name)
            .ToListAsync(cancellationToken);

    // El filtro por PlayerId es lo que impide ver un deck ajeno aunque se conozca su Id.
    public Task<Deck?> FindAsync(Guid playerId, Guid deckId, CancellationToken cancellationToken) =>
        db.Decks.AsNoTracking()
            .Include(deck => deck.Cards)
            .SingleOrDefaultAsync(deck => deck.PlayerId == playerId && deck.Id == deckId, cancellationToken);

    private sealed class CreationScope(
        PokeArenaDbContext db,
        IDbContextTransaction transaction,
        Guid playerId) : IDeckCreationScope
    {
        public Task<Deck?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
            db.Decks.AsNoTracking()
                .Include(deck => deck.Cards)
                .SingleOrDefaultAsync(
                    deck => deck.PlayerId == playerId && deck.IdempotencyKey == idempotencyKey,
                    cancellationToken);

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken)
        {
            // Se compara en minúsculas para que "Fuego" y "fuego" cuenten como el mismo nombre.
            var lowered = name.ToLowerInvariant();
            return db.Decks.AsNoTracking()
                .AnyAsync(deck => deck.PlayerId == playerId && deck.Name.ToLower() == lowered, cancellationToken);
        }

        // Solo anota. EF agrega también las 12 DeckCard porque están en deck.Cards.
        public void Add(Deck deck) => db.Decks.Add(deck);

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            try
            {
                // INSERT del deck y de sus 12 cartas.
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
            {
                // Un índice único rechazó el deck (la llave o el nombre ya existían).
                throw new DeckAlreadyExistsException(playerId, postgres.ConstraintName, exception);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
