using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using PokeArena.Api.Modules.Catalog.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Catalog.Http;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapExploreCatalog(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/cards/catalog", ExploreCatalogAsync)
            .WithName("ExploreCatalog")
            .WithSummary("Explora el catálogo disponible con filtros y posesión.")
            .WithDescription("Devuelve cartas disponibles y la cantidad que posee el jugador autenticado.")
            .RequireAuthorization()
            .Produces<CatalogResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> ExploreCatalogAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        ICatalogService service,
        CancellationToken cancellationToken)
    {
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(subject, out var playerId))
            return Results.Unauthorized();

        var parsed = CatalogQueryParser.Parse(request.Query, playerId);
        if (parsed.Error is { } error)
            return ProblemResults.From(error);

        var response = await service.ExecuteAsync(parsed.Query!, cancellationToken);
        return Results.Ok(response);
    }
}
