namespace PokeArena.GameEngine;

public sealed record MatchDeck(Guid PlayerId, IReadOnlyList<int> CardIds);

public sealed record StartMatchRequest(
    Guid MatchId,
    Guid ChallengerId,
    Guid OpponentId,
    MatchDeck ChallengerDeck,
    MatchDeck OpponentDeck,
    int LocationId);

public sealed record MatchParticipantSnapshot(Guid PlayerId, IReadOnlyList<int> CardIds);

public sealed record MatchSnapshot(
    Guid MatchId,
    Guid ChallengerId,
    Guid OpponentId,
    MatchParticipantSnapshot Challenger,
    MatchParticipantSnapshot Opponent,
    int LocationId,
    string Status,
    Guid CurrentTurnPlayerId);

public interface IGameEngine
{
    Task<MatchSnapshot> StartAsync(StartMatchRequest request, CancellationToken cancellationToken);
}

// Implementación inicial que crea el estado persistible sin aplicar reglas de combate.
// Debe sustituirse por el motor real cuando sus reglas estén disponibles.
public sealed class InitialMatchGameEngine : IGameEngine
{
    public Task<MatchSnapshot> StartAsync(
        StartMatchRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ChallengerId == request.OpponentId)
            throw new ArgumentException("El jugador no puede iniciar una partida contra sí mismo.");
        if (request.ChallengerDeck.CardIds.Count == 0 ||
            request.OpponentDeck.CardIds.Count == 0)
            throw new ArgumentException("Ambos mazos deben contener al menos una carta.");
        if (request.LocationId <= 0)
            throw new ArgumentException("La ubicación debe tener un identificador válido.");

        var snapshot = new MatchSnapshot(
            request.MatchId,
            request.ChallengerId,
            request.OpponentId,
            new MatchParticipantSnapshot(request.ChallengerId, request.ChallengerDeck.CardIds),
            new MatchParticipantSnapshot(request.OpponentId, request.OpponentDeck.CardIds),
            request.LocationId,
            "Created",
            request.ChallengerId);

        return Task.FromResult(snapshot);
    }
}
