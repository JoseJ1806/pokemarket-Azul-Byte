using PokeArena.Api.Modules.Matches.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Matches.Application;

public sealed record StartMatchCommand(
    string IdempotencyKey,
    Guid ChallengerPlayerId,
    Guid OpponentPlayerId,
    IReadOnlyList<int> ChallengerDeckCardIds,
    IReadOnlyList<int> OpponentDeckCardIds,
    int LocationId);

public sealed record StartMatchResult(
    Match? Match,
    bool IsReplay,
    DomainError? Error);

public interface IMatchRepository
{
    Task<Match?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);
    Task<bool> PlayerExistsAsync(Guid playerId, CancellationToken cancellationToken);
    Task<bool> LocationIsAvailableAsync(int locationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> GetOwnedCardIdsAsync(Guid playerId, IReadOnlyList<int> cardIds, CancellationToken cancellationToken);
    Task<IMatchCreationScope> BeginCreationAsync(string idempotencyKey, CancellationToken cancellationToken);
}

public interface IMatchCreationScope : IAsyncDisposable
{
    Task<Match?> FindExistingAsync(CancellationToken cancellationToken);
    Task CommitAsync(Match match, CancellationToken cancellationToken);
}
