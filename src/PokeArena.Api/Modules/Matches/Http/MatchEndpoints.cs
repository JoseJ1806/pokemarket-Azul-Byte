using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using PokeArena.Api.Modules.Matches.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Matches.Http;

public static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/matches", StartMatchAsync)
            .WithName("StartMatch")
            .WithTags("Matches")
            .WithSummary("Inicia una partida entre dos jugadores.")
            .RequireAuthorization()
            .Produces<StartMatchResponse>(StatusCodes.Status201Created)
            .Produces<StartMatchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<IResult> StartMatchAsync(
        HttpRequest httpRequest,
        ClaimsPrincipal user,
        StartMatchRequest request,
        StartMatchUseCase useCase,
        CancellationToken cancellationToken)
    {
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(subject, out var challengerPlayerId))
            return Results.Unauthorized();

        var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString();
        var command = new StartMatchCommand(
            idempotencyKey,
            challengerPlayerId,
            request.OpponentPlayerId,
            request.ChallengerDeckCardIds,
            request.OpponentDeckCardIds,
            request.LocationId);

        var result = await useCase.ExecuteAsync(command, cancellationToken);
        if (result.Error is { } error)
            return ProblemResults.From(error);

        var response = StartMatchResponse.From(result.Match!);
        return result.IsReplay
            ? Results.Ok(response)
            : Results.Created($"/api/v1/matches/{response.MatchId}", response);
    }
}
