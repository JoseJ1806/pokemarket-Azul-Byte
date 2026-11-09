using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using PokeArena.Api.Modules.Collection.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Collection.Http;

// ADAPTADOR DE ENTRADA HTTP del módulo Colección.
//
// Igual que AuthEndpoints en Identidad: traduce la solicitud HTTP a una llamada al caso de
// uso y el resultado a una respuesta HTTP. NO contiene reglas de negocio.
//
// Endpoint que publica:
// GET /api/me/collection  Devuelve el saldo de cubos y las cartas del jugador dueño del
//                         token, agrupadas por carta. Exige estar autenticado.
public static class CollectionEndpoints
{
    public static IEndpointRouteBuilder MapGetMyCollection(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/collection", GetMyCollectionAsync)
            .WithName("GetMyCollection")
            // Sin política: basta con un token válido de PokéArena. Sin token, ASP.NET
            // responde 401 antes de llegar aquí.
            .RequireAuthorization()
            .Produces<CollectionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    // GET /api/me/collection
    // ClaimsPrincipal user: ASP.NET lo llena con los datos del JWT ya validado.
    private static async Task<IResult> GetMyCollectionAsync(
        ClaimsPrincipal user,
        GetCollectionUseCase useCase,
        CancellationToken cancellationToken)
    {
        // Al validar el token, ASP.NET renombra el claim "sub" a ClaimTypes.NameIdentifier.
        // Se buscan los dos nombres por si esa conversión se desactiva algún día.
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        var result = await useCase.ExecuteAsync(subject, cancellationToken);

        return result.Error is { } error
            ? ProblemResults.From(error)
            : Results.Ok(CollectionResponse.From(result.Collection!));
    }
}
