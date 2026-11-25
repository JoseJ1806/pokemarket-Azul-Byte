using Microsoft.Extensions.Logging;
using PokeArena.Api.Modules.Matchmaking.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Matchmaking.Application;

// Lo que pide el cliente: el deck. Subject es el claim "sub" del JWT, de ahí sale el jugador.
public sealed record SearchMatchCommand(string? Subject, Guid? DeckId);

// CASO DE USO: BUSCAR PARTIDA (US #127, módulo Emparejamiento).
//
// Orden del flujo:
// 1. Valida el jugador y el deck: que lo indicó, que es suyo y que cumple RN-14. Solo lee.
// 2. Con el candado del jugador (transacción en PostgreSQL):
//    a. Si ya tiene una búsqueda abierta con el MISMO deck, la devuelve (doble clic, dos
//       pestañas o un reintento dejan una sola búsqueda). Con otro deck o en otro contexto,
//       se rechaza indicando la búsqueda vigente (RN-16).
//    b. Rechaza si tiene una partida activa (RN-16) o menos de 8 cubos (RN-15).
//    c. Guarda la búsqueda nueva en espera.
// 3. Intenta emparejarla de inmediato (MatchmakingPairing).
//
// La regla de una sola búsqueda abierta tiene dos barreras, como Crear Deck: el candado por
// jugador y el índice único parcial de match_searches (ADR-007).
public sealed class SearchMatchUseCase(
    IMatchSearchRepository searches,
    ISearchDeckLookup decks,
    ICubeBalanceLookup cubes,
    IPlayerEloLookup players,
    IActiveMatchLookup activeMatches,
    MatchmakingPairing pairing,
    TimeProvider clock,
    ILogger<SearchMatchUseCase> logger)
{
    public async Task<MatchSearchResult> ExecuteAsync(SearchMatchCommand command, CancellationToken cancellationToken)
    {
        // ---- PASO 1. Jugador y deck (solo lecturas) ----
        if (!Guid.TryParse(command.Subject, out var playerId))
            return MatchSearchResult.Failed(MatchmakingErrors.NotAPlayer());

        var elo = await players.FindEloAsync(playerId, cancellationToken);
        if (elo is null)
            return MatchSearchResult.Failed(MatchmakingErrors.NotAPlayer());

        if (command.DeckId is not { } deckId || deckId == Guid.Empty)
            return MatchSearchResult.Failed(DomainError.Validation(
                "deck-required",
                "Debes seleccionar uno de tus decks para buscar partida.",
                "deckId"));

        // Un deck de otro jugador se trata igual que uno inexistente (como en Crear Deck).
        var deck = await decks.FindAsync(playerId, deckId, cancellationToken);
        if (deck is null)
            return MatchSearchResult.Failed(DomainError.NotFound(
                "deck-not-found",
                "El deck no existe o no es tuyo.",
                "deckId"));

        var deckProblem = MatchmakingRules.DeckProblem(
            deck.CardIds.Count,
            deck.CardIds.Distinct().Count(),
            deck.OwnedCardCount);
        if (deckProblem is not null)
            return MatchSearchResult.Failed(DomainError.Validation("invalid-deck", deckProblem, "deckId"));

        // ---- PASO 2. Con el candado del jugador ----
        var now = clock.GetUtcNow();
        MatchSearch created;
        await using (var scope = await searches.BeginPlayerScopeAsync(playerId, cancellationToken))
        {
            var open = await scope.FindOpenAsync(cancellationToken);

            // Una búsqueda que el jugador abandonó ya no cuenta: se marca como expirada antes de
            // guardar la nueva. Se guarda primero porque el índice único de búsquedas abiertas
            // rechazaría la nueva mientras la vieja siga en espera.
            if (open is not null && open.IsInactive(now))
            {
                open.Expire(now);
                await scope.SaveAsync(cancellationToken);
                open = null;
            }

            if (open is not null)
            {
                if (open.Context == MatchmakingRules.RankedContext && open.DeckId == deck.DeckId)
                {
                    // La misma búsqueda otra vez: se devuelve la que ya existe.
                    open.Touch(now);
                    await scope.CommitAsync(cancellationToken);
                    return MatchSearchResult.Replayed(MatchSearchView.From(open, now));
                }

                return MatchSearchResult.Failed(OpenSearchConflict(open));
            }

            var activeMatchId = await activeMatches.FindActiveMatchIdAsync(playerId, cancellationToken);
            if (activeMatchId is { } matchId)
                return MatchSearchResult.Failed(MatchmakingErrors.Conflict(
                    "active-match",
                    $"Tienes una partida activa ({matchId}). No puedes buscar otra partida hasta terminarla."));

            var balance = await cubes.GetBalanceAsync(playerId, cancellationToken);
            if (balance < MatchmakingRules.MinimumCubes)
                return MatchSearchResult.Failed(DomainError.Validation(
                    "insufficient-cubes",
                    $"Tienes {balance} cubos y necesitas al menos {MatchmakingRules.MinimumCubes} para buscar " +
                    "partida clasificatoria. Compra cubos para continuar.",
                    "cubes"));

            created = MatchSearch.Open(
                playerId,
                MatchmakingRules.RankedContext,
                deck.DeckId,
                deck.CardIds,
                elo.Value,
                now);
            scope.Add(created);

            try
            {
                await scope.CommitAsync(cancellationToken);
            }
            catch (OpenSearchAlreadyExistsException)
            {
                // Última barrera: otra solicitud del jugador guardó una búsqueda abierta.
                logger.LogInformation("El jugador {PlayerId} ya tenía una búsqueda abierta.", playerId);
                return MatchSearchResult.Failed(MatchmakingErrors.Conflict(
                    "search-already-open",
                    "Ya tienes una búsqueda abierta. Consulta tu búsqueda para ver su estado."));
            }
        }

        logger.LogInformation(
            "El jugador {PlayerId} busca partida con el deck {DeckId} (Elo {Elo}).",
            playerId, deck.DeckId, elo.Value);

        // ---- PASO 3. Intentar emparejar de una vez ----
        var current = await pairing.ProcessAsync(created.Id, markSeen: false, cancellationToken) ?? created;
        return MatchSearchResult.Ok(MatchSearchView.From(current, clock.GetUtcNow()));
    }

    // #127: el rechazo indica la búsqueda vigente.
    private static DomainError OpenSearchConflict(MatchSearch open)
    {
        var message = open.Context == MatchmakingRules.RankedContext
            ? $"Ya tienes una búsqueda abierta ({open.Id}) con otro deck. Para usar otro deck, sal de la " +
              "búsqueda y busca de nuevo."
            : $"Ya tienes una búsqueda abierta de tipo {open.Context} ({open.Id}). Solo puedes tener una " +
              "búsqueda abierta a la vez.";
        return MatchmakingErrors.Conflict("search-already-open", message);
    }
}
