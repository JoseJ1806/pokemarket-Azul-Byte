using PokeArena.Api.Modules.Matchmaking.Application;
using PokeArena.Api.Modules.Matchmaking.Domain;

namespace PokeArena.Api.Modules.Matchmaking.Http;

// CUERPO de POST /api/me/match-search. Solo trae el deck: el jugador sale del token.
public sealed record SearchMatchRequest(Guid? DeckId);

// RESPUESTA de buscar, consultar y salir de la búsqueda.
//
// Status:           Waiting, MatchFound, Cancelled o Expired (modelo de estado de la V3).
// MatchId:          la partida asignada cuando Status es MatchFound.
// CreatingMatch:    se encontró rival y se está creando la partida (Status sigue en Waiting).
// WaitedSeconds:    tiempo de espera.
// EloRange:         diferencia de Elo que acepta la búsqueda en este momento.
// BotOfferAvailable: a los 60 segundos sin rival se ofrece el bot (RN-17).
// Message:          aviso para el jugador, por ejemplo por qué volvió a la búsqueda.
// PollIntervalSeconds e InactivityTimeoutSeconds: cada cuánto consultar y cuánto se puede
//                   dejar de consultar antes de quedar fuera de la búsqueda.
public sealed record MatchSearchResponse(
    Guid SearchId,
    string Status,
    string Context,
    Guid DeckId,
    Guid? MatchId,
    bool CreatingMatch,
    int WaitedSeconds,
    int EloRange,
    bool BotOfferAvailable,
    string? Message,
    int PollIntervalSeconds,
    int InactivityTimeoutSeconds,
    DateTimeOffset CreatedAt)
{
    public static MatchSearchResponse From(MatchSearchView view) =>
        new(
            view.SearchId,
            view.Status,
            view.Context,
            view.DeckId,
            view.MatchId,
            view.CreatingMatch,
            view.WaitedSeconds,
            view.EloRange,
            view.BotOfferAvailable,
            view.Notice,
            (int)MatchmakingRules.PollInterval.TotalSeconds,
            (int)MatchmakingRules.InactivityTimeout.TotalSeconds,
            view.CreatedAt);
}
