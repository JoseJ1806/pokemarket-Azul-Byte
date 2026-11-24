using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using PokeArena.Api.Modules.Collection.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Collection.Http;

// ADAPTADOR DE ENTRADA HTTP de los decks (módulo Colección).
//
// Igual que CollectionEndpoints: traduce la solicitud HTTP a una llamada al caso de uso y
// el resultado a una respuesta HTTP. NO contiene reglas de negocio.
//
// Endpoints que publica (todos exigen estar autenticado; el jugador sale del token):
// POST /api/me/decks            Crea un deck. Exige el encabezado Idempotency-Key.
//                               201 si se creó, 200 si la misma llave ya lo había creado.
// GET  /api/me/decks            Lista los decks del jugador.
// GET  /api/me/decks/{deckId}   Devuelve un deck con sus cartas. 404 si no existe o es ajeno.
public static class DeckEndpoints
{
    public static IEndpointRouteBuilder MapDeckEndpoints(this IEndpointRouteBuilder app)
    {
        // El grupo aplica RequireAuthorization a las tres rutas: sin token válido, 401.
        var group = app.MapGroup("/api/me/decks").RequireAuthorization();

        group.MapPost(string.Empty, CreateDeckAsync)
            .WithName("CreateDeck")
            .Produces<DeckResponse>(StatusCodes.Status201Created)
            .Produces<DeckResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet(string.Empty, ListDecksAsync)
            .WithName("ListMyDecks")
            .Produces<IReadOnlyList<DeckResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{deckId:guid}", GetDeckAsync)
            .WithName("GetMyDeck")
            .Produces<DeckDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateDeckAsync(
        ClaimsPrincipal user,
        HttpRequest httpRequest,
        CreateDeckRequest request,
        CreateDeckUseCase useCase,
        CancellationToken cancellationToken)
    {
        var command = new CreateDeckCommand(
            GetSubject(user),
            httpRequest.Headers["Idempotency-Key"].ToString(),
            request.Name,
            request.CardIds);

        var result = await useCase.ExecuteAsync(command, cancellationToken);
        if (result.Error is { } error)
            return ProblemResults.From(error);

        var response = DeckResponse.From(result.Deck!);
        return result.IsReplay
            ? Results.Ok(response)
            : Results.Created($"/api/me/decks/{response.Id}", response);
    }

    private static async Task<IResult> ListDecksAsync(
        ClaimsPrincipal user,
        ListDecksUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(GetSubject(user), cancellationToken);
        return result.Error is { } error
            ? ProblemResults.From(error)
            : Results.Ok(result.Decks!.Select(DeckResponse.From).ToList());
    }

    private static async Task<IResult> GetDeckAsync(
        Guid deckId,
        ClaimsPrincipal user,
        GetDeckUseCase useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(GetSubject(user), deckId, cancellationToken);
        return result.Error is { } error
            ? ProblemResults.From(error)
            : Results.Ok(DeckDetailResponse.From(result.Deck!));
    }

    // Al validar el token, ASP.NET renombra el claim "sub" a ClaimTypes.NameIdentifier.
    // Se buscan los dos nombres por si esa conversión se desactiva algún día.
    private static string? GetSubject(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
}
