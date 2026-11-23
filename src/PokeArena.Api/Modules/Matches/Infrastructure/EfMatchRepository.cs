using Microsoft.EntityFrameworkCore;
using PokeArena.Api.Data;
using PokeArena.Api.Modules.Matches.Application;
using PokeArena.Api.Modules.Matches.Domain;

namespace PokeArena.Api.Modules.Matches.Infrastructure;

public sealed class EfMatchRepository(PokeArenaDbContext db) : IMatchRepository
{
    public Task<Match?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        db.Matches.AsNoTracking().SingleOrDefaultAsync(match => match.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<bool> PlayerExistsAsync(Guid playerId, CancellationToken cancellationToken) =>
        db.PlayerAccounts.AsNoTracking().AnyAsync(player => player.Id == playerId, cancellationToken);

    public Task<bool> LocationIsAvailableAsync(int locationId, CancellationToken cancellationToken) =>
        db.Locations.AsNoTracking().AnyAsync(location => location.Id == locationId && location.IsAvailable, cancellationToken);

    public async Task<IReadOnlyList<int>> GetOwnedCardIdsAsync(
        Guid playerId,
        IReadOnlyList<int> cardIds,
        CancellationToken cancellationToken)
    {
        var uniqueIds = cardIds.Distinct().ToArray();
        var owned = await db.CardCopies.AsNoTracking()
            .Where(copy => copy.PlayerId == playerId && uniqueIds.Contains(copy.CardId))
            .Select(copy => copy.CardId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return owned;
    }

    public async Task<IMatchCreationScope> BeginCreationAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext({0}))",
                new object[] { "match-idempotency:" + idempotencyKey },
                cancellationToken);
            return new CreationScope(db, transaction, idempotencyKey);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class CreationScope(
        PokeArenaDbContext db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string idempotencyKey) : IMatchCreationScope
    {
        public Task<Match?> FindExistingAsync(CancellationToken cancellationToken) =>
            db.Matches.AsNoTracking().SingleOrDefaultAsync(match => match.IdempotencyKey == idempotencyKey, cancellationToken);

        public async Task CommitAsync(Match match, CancellationToken cancellationToken)
        {
            db.Matches.Add(match);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
