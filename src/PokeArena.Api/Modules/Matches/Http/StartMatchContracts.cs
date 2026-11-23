using System.Text.Json;
using PokeArena.Api.Modules.Matches.Domain;

namespace PokeArena.Api.Modules.Matches.Http;

public sealed class StartMatchRequest
{
    public Guid OpponentPlayerId { get; set; }
    public IReadOnlyList<int> ChallengerDeckCardIds { get; set; } = [];
    public IReadOnlyList<int> OpponentDeckCardIds { get; set; } = [];
    public int LocationId { get; set; }
}

public sealed record StartMatchResponse(
    Guid MatchId,
    Guid ChallengerPlayerId,
    Guid OpponentPlayerId,
    IReadOnlyList<int> ChallengerDeckCardIds,
    IReadOnlyList<int> OpponentDeckCardIds,
    int LocationId,
    string Status,
    Guid CurrentTurnPlayerId)
{
    public static StartMatchResponse From(Match match) =>
        new(
            match.Id,
            match.ChallengerPlayerId,
            match.OpponentPlayerId,
            JsonSerializer.Deserialize<IReadOnlyList<int>>(match.ChallengerDeckCardIds) ?? [],
            JsonSerializer.Deserialize<IReadOnlyList<int>>(match.OpponentDeckCardIds) ?? [],
            match.LocationId,
            match.Status,
            match.CurrentTurnPlayerId);
}
