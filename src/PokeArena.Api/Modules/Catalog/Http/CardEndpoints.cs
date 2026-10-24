using Microsoft.AspNetCore.Mvc;
using PokeArena.Api.Modules.Catalog.Application;
using PokeArena.Api.Modules.Identity;
using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Catalog.Http;

// ADAPTADOR DE ENTRADA HTTP del módulo Catálogo.
// Traduce la solicitud multipart al comando del caso de uso y el resultado a HTTP.
// No contiene reglas de negocio: esas viven en CreateCardUseCase y CardRules.
public static class CardEndpoints
{
    public static IEndpointRouteBuilder MapCreateCard(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/cards", CreateCardAsync)
            .WithName("CreateCard")
            .DisableAntiforgery()
            .RequireAuthorization(AdminAuthorization.PolicyName)
            .Produces<CardResponse>(StatusCodes.Status201Created)
            .Produces<CardResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    // 201 cuando se crea la carta y 200 cuando la misma Idempotency-Key ya la había creado.
    private static async Task<IResult> CreateCardAsync(
        [FromForm] CreateCardRequest request,
        HttpRequest httpRequest,
        CreateCardUseCase useCase,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString();

        ImageUpload? image = null;
        if (request.Image is { } file)
        {
            // Un archivo que excede el peso máximo no se lee a memoria: el caso de uso lo rechaza.
            var content = file.Length is > 0 and <= ImageRequirements.MaxFileSize
                ? await ReadAllBytesAsync(file, cancellationToken)
                : Array.Empty<byte>();
            image = new ImageUpload(file.ContentType, file.Length, content);
        }

        var command = new CreateCardCommand(
            idempotencyKey,
            request.Name,
            request.Cost,
            request.Power,
            request.Type,
            request.Rarity,
            request.AbilityCode,
            image);

        var result = await useCase.ExecuteAsync(command, cancellationToken);
        if (result.Error is { } error)
            return ProblemResults.From(error);

        var response = CardResponse.From(result.Card!);
        return result.IsReplay
            ? Results.Ok(response)
            : Results.Created($"/api/cards/{response.Id}", response);
    }

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
