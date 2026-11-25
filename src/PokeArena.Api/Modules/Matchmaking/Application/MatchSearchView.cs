using PokeArena.Api.Modules.Matchmaking.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Matchmaking.Application;

// LO QUE VE EL JUGADOR DE SU BÚSQUEDA (módulo Emparejamiento, capa de aplicación).
//
// Status usa los cuatro estados del modelo de la V3: Waiting, MatchFound, Cancelled y Expired.
// El estado interno Pairing se muestra como Waiting con CreatingMatch = true.
public sealed record MatchSearchView(
    Guid SearchId,
    string Status,
    string Context,
    Guid DeckId,
    Guid? MatchId,
    bool CreatingMatch,
    int WaitedSeconds,
    int EloRange,
    bool BotOfferAvailable,
    string? Notice,
    DateTimeOffset CreatedAt)
{
    public static MatchSearchView From(MatchSearch search, DateTimeOffset now)
    {
        var waited = search.WaitedAt(now);
        var waiting = search.Status is MatchSearchStatus.Waiting or MatchSearchStatus.Pairing;
        var status = waiting ? nameof(MatchSearchStatus.Waiting) : search.Status.ToString();

        return new MatchSearchView(
            search.Id,
            status,
            search.Context,
            search.DeckId,
            search.MatchId,
            search.Status == MatchSearchStatus.Pairing,
            (int)Math.Floor(waited.TotalSeconds),
            MatchmakingRules.EloRangeFor(waited),
            // RN-17: a los 60 segundos sin rival se ofrece el bot. En torneos no se ofrece.
            search.Status == MatchSearchStatus.Waiting
                && search.Context == MatchmakingRules.RankedContext
                && waited >= MatchmakingRules.BotOfferAfter,
            search.Notice,
            search.CreatedAt);
    }
}

// Resultado de los casos de uso: la búsqueda, si es una repetición y el error de negocio.
public sealed record MatchSearchResult(MatchSearchView? Search, bool IsReplay, DomainError? Error)
{
    public static MatchSearchResult Ok(MatchSearchView search) => new(search, false, null);

    public static MatchSearchResult Replayed(MatchSearchView search) => new(search, true, null);

    public static MatchSearchResult Failed(DomainError error) => new(null, false, error);
}

// Errores comunes de los tres casos de uso.
internal static class MatchmakingErrors
{
    public const string NotAPlayerCode = "player-not-found";

    public static DomainError NotAPlayer() =>
        DomainError.NotFound(NotAPlayerCode, "La sesión actual no corresponde a una cuenta de jugador.");

    public static DomainError NoSearch() =>
        DomainError.NotFound("search-not-found", "No tienes una búsqueda de partida abierta.");

    public static DomainError Conflict(string code, string message, string? field = null) =>
        new(DomainErrorKind.Conflict, code, message, field);
}
