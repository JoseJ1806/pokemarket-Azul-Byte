using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using PokeArena.Api.Modules.Matchmaking.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Matchmaking.Http;

// ADAPTADOR DE ENTRADA HTTP del emparejamiento (US #127 y #128).
//
// Traduce cada solicitud a una llamada al caso de uso y el resultado a una respuesta HTTP.
// NO contiene reglas de negocio. Todas las rutas exigen sesión: el jugador sale del token.
//
// POST   /api/me/match-search   Busca partida con un deck: { "deckId": "..." }.
//                               201 si se abrió la búsqueda, 200 si ya estaba abierta con
//                               ese deck (doble clic, otra pestaña o reintento).
// GET    /api/me/match-search   Estado de la búsqueda más reciente. Mientras está abierta,
//                               la pantalla lo consulta cada PollIntervalSeconds.
// DELETE /api/me/match-search   Sale de la búsqueda abierta.
public static class MatchmakingEndpoints
{
    public static IEndpointRouteBuilder MapMatchSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/match-search")
            .RequireAuthorization()
            .WithTags("Matchmaking");

        group.MapPost(string.Empty, SearchAsync)
            .WithName("SearchMatch")
            .WithSummary("Busca una partida clasificatoria con uno de los decks del jugador.")
            .Produces<MatchSearchResponse>(StatusCodes.Status201Created)
            .Produces<MatchSearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet(string.Empty, GetAsync)
            .WithName("GetMyMatchSearch")
            .WithSummary("Consulta el estado de la búsqueda de partida.")
            .Produces<MatchSearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete(string.Empty, CancelAsync)
            .WithName("CancelMyMatchSearch")
            .WithSummary("Sale de la búsqueda de partida.")
            .Produces<MatchSearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> SearchAsync(
        ClaimsPrincipal user,
        SearchMatchRequest? request,
        SearchMatchUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new SearchMatchCommand(GetSubject(user), request?.DeckId),
            cancellationToken);
        if (result.Error is { } error)
            return ProblemResults.From(error);

        var response = MatchSearchResponse.From(result.Search!);
        return result.IsReplay
            ? Results.Ok(response)
            : Results.Created("/api/me/match-search", response);
    }

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        GetMatchSearchUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(GetSubject(user), cancellationToken);
        return result.Error is { } error
            ? ProblemResults.From(error)
            : Results.Ok(MatchSearchResponse.From(result.Search!));
    }

    private static async Task<IResult> CancelAsync(
        ClaimsPrincipal user,
        CancelMatchSearchUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(GetSubject(user), cancellationToken);
        return result.Error is { } error
            ? ProblemResults.From(error)
            : Results.Ok(MatchSearchResponse.From(result.Search!));
    }

    // Al validar el token, ASP.NET renombra el claim "sub" a ClaimTypes.NameIdentifier.
    // Se buscan los dos nombres, igual que en DeckEndpoints.
    private static string? GetSubject(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
}
