using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PokeArena.Api.Data;
using PokeArena.Api.Models;
using PokeArena.Api.Modules.Catalog.Application;

namespace PokeArena.Api.Modules.Catalog.Infrastructure;

// ADAPTADOR RepositorioCartas.
// Implementa el puerto ICardRepository con Entity Framework y PostgreSQL. Los bloqueos
// consultivos (advisory locks) hacen que una llave de idempotencia y un tipo de carta se
// atiendan de uno en uno, aunque lleguen a instancias distintas de la API. Por eso basta
// el aislamiento por defecto y no se producen fallos de serialización entre tipos distintos.
public sealed class EfCardRepository(PokeArenaDbContext db) : ICardRepository
{
    public Task<Card?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        db.Cards.AsNoTracking().SingleOrDefaultAsync(card => card.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<Ability?> FindAbilityAsync(string code, CancellationToken cancellationToken) =>
        db.Abilities.AsNoTracking().SingleOrDefaultAsync(ability => ability.Code == code, cancellationToken);

    public async Task<ICardCreationScope> BeginCreationAsync(
        string idempotencyKey,
        string typePrefix,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Siempre se toman en este orden para no provocar bloqueos cruzados.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext({0}))",
                new object[] { "card-idempotency:" + idempotencyKey },
                cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext({0}))",
                new object[] { "card-code:" + typePrefix },
                cancellationToken);

            return new CreationScope(db, transaction, idempotencyKey, typePrefix);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class CreationScope(
        PokeArenaDbContext db,
        IDbContextTransaction transaction,
        string idempotencyKey,
        string typePrefix) : ICardCreationScope
    {
        public Task<Card?> FindExistingAsync(CancellationToken cancellationToken)
        {
            var key = idempotencyKey;
            return db.Cards.AsNoTracking().SingleOrDefaultAsync(card => card.IdempotencyKey == key, cancellationToken);
        }

        public async Task<string> ReserveNextCodeAsync(CancellationToken cancellationToken)
        {
            var prefix = typePrefix;
            var codes = await db.Cards
                .Where(card => card.Code.Contains("-" + prefix + "-"))
                .Select(card => card.Code)
                .ToListAsync(cancellationToken);

            var next = codes
                .Select(code => int.TryParse(code[(code.LastIndexOf('-') + 1)..], out var number) ? number : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            return $"PKA-{prefix}-{next:000}";
        }

        public async Task<bool> EvolutionCreatesCycleAsync(
            string newCardCode,
            string targetCardCode,
            CancellationToken cancellationToken)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var currentCode = targetCardCode;

            while (!string.IsNullOrWhiteSpace(currentCode))
            {
                if (currentCode.Equals(newCardCode, StringComparison.Ordinal) || !visited.Add(currentCode))
                    return true;

                var targetAbilityCode = await db.Cards
                    .Where(card => card.Code == currentCode)
                    .Select(card => card.AbilityCode)
                    .SingleOrDefaultAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(targetAbilityCode))
                    return false;

                currentCode = await db.Abilities
                    .Where(ability => ability.Code == targetAbilityCode)
                    .Select(ability => ability.TargetCardCode)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return false;
        }

        public async Task CommitAsync(Card card, CancellationToken cancellationToken)
        {
            db.Cards.Add(card);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
