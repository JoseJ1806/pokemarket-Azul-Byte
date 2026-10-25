using Microsoft.AspNetCore.Mvc;
using PokeArena.Api.Modules.Identity;
using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Modules.Locations.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Locations.Http;

// ADAPTADOR DE ENTRADA HTTP del módulo Ubicaciones.
// Traduce la solicitud multipart al comando del caso de uso y el resultado a HTTP.
// No contiene reglas de negocio: esas viven en CreateLocationUseCase y LocationRules.
public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapCreateLocation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/locations", CreateLocationAsync)
            .WithName("CreateLocation")
            .WithTags("Locations")
            .WithSummary("Crea una ubicación de juego.")
            .WithDescription(
                "Solo para administradores. Requiere multipart/form-data, el encabezado Idempotency-Key y una "
                + "imagen horizontal 4:3 (PNG, JPG o WebP, máximo 5 MB, mínimo 800 x 600). "
                + "Devuelve 201 al crear y 200 si la misma Idempotency-Key ya había creado la ubicación. "
                + "Errores 400 (application/problem+json con los campos code y field): "
                + "missing-idempotency-key, required-field, invalid-field, invalid-image, "
                + "invalid-effect-type e invalid-effect-parameters. "
                + "Error 409: idempotency-key-conflict cuando la llave se reutiliza con datos distintos. "
                + "Una ubicación cuya versión pequeña falló en todos los intentos se guarda con ImageStatus "
                + "Fallida y no queda disponible. Tipos de efecto: BonificacionPorTipo, ModificadorDeCosto y "
                + "RestriccionPorTurno, con parámetros JSON.")
            .DisableAntiforgery()
            .RequireAuthorization(AdminAuthorization.PolicyName)
            .Produces<LocationResponse>(StatusCodes.Status201Created)
            .Produces<LocationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    // 201 cuando se crea la ubicación y 200 cuando la misma Idempotency-Key ya la había creado.
    private static async Task<IResult> CreateLocationAsync(
        [FromForm] CreateLocationRequest request,
        HttpRequest httpRequest,
        CreateLocationUseCase useCase,
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

        var command = new CreateLocationCommand(
            idempotencyKey,
            request.Name,
            request.Description,
            request.CardLimit,
            request.EnergyMode,
            request.EnergyAmount,
            request.EffectType,
            request.EffectParameters,
            image);

        var result = await useCase.ExecuteAsync(command, cancellationToken);
        if (result.Error is { } error)
            return ProblemResults.From(error);

        var response = LocationResponse.From(result.Location!);
        return result.IsReplay
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}