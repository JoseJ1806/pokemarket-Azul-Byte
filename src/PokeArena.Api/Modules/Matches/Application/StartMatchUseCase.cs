using System.Text.Json;
using PokeArena.Api.Modules.Matches.Domain;
using PokeArena.Api.Shared;
using PokeArena.GameEngine;

namespace PokeArena.Api.Modules.Matches.Application;

public sealed class StartMatchUseCase(IMatchRepository matches, IGameEngine engine)
{
    public async Task<StartMatchResult> ExecuteAsync(
        StartMatchCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return Failed(DomainError.Validation(
                "missing-idempotency-key",
                "Se requiere el encabezado Idempotency-Key.",
                "Idempotency-Key"));
        if (command.ChallengerPlayerId == Guid.Empty)
            return Failed(DomainError.Validation("invalid-player", "El jugador autenticado no es válido.", "challengerPlayerId"));
        if (command.OpponentPlayerId == Guid.Empty)
            return Failed(DomainError.Validation("invalid-player", "El rival no es válido.", "opponentPlayerId"));
        if (command.OpponentPlayerId == command.ChallengerPlayerId)
            return Failed(DomainError.Validation("invalid-opponent", "Un jugador no puede iniciar una partida contra sí mismo.", "opponentPlayerId"));
        if (command.LocationId <= 0)
            return Failed(DomainError.Validation("invalid-location", "La ubicación no es válida.", "locationId"));

        var challengerDeckError = ValidateDeck(command.ChallengerDeckCardIds, "challengerDeckCardIds");
        if (challengerDeckError is not null)
            return Failed(challengerDeckError);
        var opponentDeckError = ValidateDeck(command.OpponentDeckCardIds, "opponentDeckCardIds");
        if (opponentDeckError is not null)
            return Failed(opponentDeckError);

        var existing = await matches.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return ReplayOrConflict(existing, command);

        if (!await matches.PlayerExistsAsync(command.ChallengerPlayerId, cancellationToken))
            return Failed(DomainError.NotFound("player-not-found", "No existe el jugador desafiante.", "challengerPlayerId"));
        if (!await matches.PlayerExistsAsync(command.OpponentPlayerId, cancellationToken))
            return Failed(DomainError.NotFound("player-not-found", "No existe el jugador rival.", "opponentPlayerId"));
        if (!await matches.LocationIsAvailableAsync(command.LocationId, cancellationToken))
            return Failed(DomainError.NotFound("location-not-found", "La ubicación no existe o no está disponible.", "locationId"));

        var challengerOwned = await matches.GetOwnedCardIdsAsync(
            command.ChallengerPlayerId,
            command.ChallengerDeckCardIds,
            cancellationToken);
        if (challengerOwned.Count != command.ChallengerDeckCardIds.Count)
            return Failed(DomainError.Validation("deck-card-not-owned", "El mazo desafiante contiene cartas que no posee el jugador.", "challengerDeckCardIds"));

        var opponentOwned = await matches.GetOwnedCardIdsAsync(
            command.OpponentPlayerId,
            command.OpponentDeckCardIds,
            cancellationToken);
        if (opponentOwned.Count != command.OpponentDeckCardIds.Count)
            return Failed(DomainError.Validation("deck-card-not-owned", "El mazo rival contiene cartas que no posee el jugador.", "opponentDeckCardIds"));

        await using var scope = await matches.BeginCreationAsync(command.IdempotencyKey, cancellationToken);
        var concurrent = await scope.FindExistingAsync(cancellationToken);
        if (concurrent is not null)
            return ReplayOrConflict(concurrent, command);

        var matchId = Guid.CreateVersion7();
        var challengerJson = JsonSerializer.Serialize(command.ChallengerDeckCardIds);
        var opponentJson = JsonSerializer.Serialize(command.OpponentDeckCardIds);

        MatchSnapshot snapshot;
        try
        {
            snapshot = await engine.StartAsync(
                new StartMatchRequest(
                    matchId,
                    command.ChallengerPlayerId,
                    command.OpponentPlayerId,
                    new MatchDeck(command.ChallengerPlayerId, command.ChallengerDeckCardIds),
                    new MatchDeck(command.OpponentPlayerId, command.OpponentDeckCardIds),
                    command.LocationId),
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Failed(DomainError.Validation("invalid-match-state", exception.Message, null));
        }
        catch (Exception)
        {
            return Failed(DomainError.Unavailable("game-engine-unavailable", "El motor de juego no pudo iniciar la partida."));
        }

        var match = Match.Create(
            snapshot.MatchId,
            snapshot.ChallengerId,
            snapshot.OpponentId,
            snapshot.LocationId,
            snapshot.Challenger.CardIds,
            snapshot.Opponent.CardIds,
            snapshot.CurrentTurnPlayerId,
            command.IdempotencyKey,
            DateTimeOffset.UtcNow,
            challengerJson,
            opponentJson);

        await scope.CommitAsync(match, cancellationToken);
        return new StartMatchResult(match, false, null);
    }

    private static DomainError? ValidateDeck(IReadOnlyList<int> cardIds, string field)
    {
        if (cardIds.Count == 0)
            return DomainError.Validation("empty-deck", "El mazo debe contener al menos una carta.", field);
        if (cardIds.Any(cardId => cardId <= 0))
            return DomainError.Validation("invalid-card-id", "Todos los IDs de cartas deben ser mayores que cero.", field);
        if (cardIds.Distinct().Count() != cardIds.Count)
            return DomainError.Validation("duplicate-card-id", "Un mazo no puede repetir el mismo ID de carta.", field);
        return null;
    }

    private static StartMatchResult ReplayOrConflict(Match existing, StartMatchCommand command)
    {
        var challengerDeckMatches = DeckMatches(
            existing.ChallengerDeckCardIds,
            command.ChallengerDeckCardIds);
        var opponentDeckMatches = DeckMatches(
            existing.OpponentDeckCardIds,
            command.OpponentDeckCardIds);
        var sameRequest =
            existing.ChallengerPlayerId == command.ChallengerPlayerId &&
            existing.OpponentPlayerId == command.OpponentPlayerId &&
            existing.LocationId == command.LocationId &&
            challengerDeckMatches &&
            opponentDeckMatches;

        return sameRequest
            ? new StartMatchResult(existing, true, null)
            : Failed(new DomainError(
                DomainErrorKind.Conflict,
                "idempotency-key-conflict",
                "La Idempotency-Key ya se usó con datos distintos.",
                "Idempotency-Key"));
    }

    private static bool DeckMatches(string storedJson, IReadOnlyList<int> requested)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<IReadOnlyList<int>>(storedJson);
            return stored is not null && stored.SequenceEqual(requested);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static StartMatchResult Failed(DomainError error) =>
        new(null, false, error);
}
