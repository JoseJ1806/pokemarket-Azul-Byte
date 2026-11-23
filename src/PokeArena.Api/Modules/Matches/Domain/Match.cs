namespace PokeArena.Api.Modules.Matches.Domain;

public sealed class Match
{
    public Guid Id { get; private set; }
    public Guid ChallengerPlayerId { get; private set; }
    public Guid OpponentPlayerId { get; private set; }
    public int LocationId { get; private set; }
    public string ChallengerDeckCardIds { get; private set; } = "[]";
    public string OpponentDeckCardIds { get; private set; } = "[]";
    public string Status { get; private set; } = "Created";
    public Guid CurrentTurnPlayerId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private Match()
    {
    }

    public static Match Create(
        Guid id,
        Guid challengerPlayerId,
        Guid opponentPlayerId,
        int locationId,
        IReadOnlyList<int> challengerDeckCardIds,
        IReadOnlyList<int> opponentDeckCardIds,
        Guid currentTurnPlayerId,
        string idempotencyKey,
        DateTimeOffset createdAt,
        string challengerDeckJson,
        string opponentDeckJson) =>
        new()
        {
            Id = id,
            ChallengerPlayerId = challengerPlayerId,
            OpponentPlayerId = opponentPlayerId,
            LocationId = locationId,
            ChallengerDeckCardIds = challengerDeckJson,
            OpponentDeckCardIds = opponentDeckJson,
            Status = "Created",
            CurrentTurnPlayerId = currentTurnPlayerId,
            IdempotencyKey = idempotencyKey,
            CreatedAt = createdAt
        };
}
