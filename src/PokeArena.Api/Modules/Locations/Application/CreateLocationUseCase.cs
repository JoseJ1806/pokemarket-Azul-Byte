using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Modules.Locations.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Locations.Application;

public sealed record CreateLocationCommand(
    string IdempotencyKey,
    string? Name,
    string? Description,
    int? CardLimit,
    string? EnergyMode,
    int? EnergyAmount,
    string? EffectType,
    string? EffectParameters,
    ImageUpload? Image);

// Resultado de crear una ubicación: creada, repetición idempotente o error de negocio.
public sealed record CreateLocationResult(Location? Location, bool IsReplay, DomainError? Error)
{
    public static CreateLocationResult Created(Location location) => new(location, false, null);

    public static CreateLocationResult Replayed(Location location) => new(location, true, null);

    public static CreateLocationResult Failed(DomainError error) => new(null, false, error);
}

// CASO DE USO: CREAR UBICACIÓN (módulo Ubicaciones).
// Las reglas de entrada viven en CreateLocationValidator; aquí solo queda el flujo:
// 1. Valida el comando (campos, configuración, efecto y llave de idempotencia).
// 2. Si la llave ya creó una ubicación, devuelve esa misma ubicación.
// 3. Valida la imagen 4:3 y prepara la versión pequeña, fuera de cualquier bloqueo.
// 4. Dentro de una unidad de trabajo exclusiva guarda las imágenes y la ubicación.
//    Si algo falla, borra los archivos para no dejar huérfanos.
public sealed class CreateLocationUseCase(
    ILocationRepository locations,
    ProcessImageUseCase images,
    LocationEffectCatalog effects)
{
    private const string ImagesFolder = "uploads/locations";

    public async Task<CreateLocationResult> ExecuteAsync(
        CreateLocationCommand command,
        CancellationToken cancellationToken)
    {
        var validation = CreateLocationValidator.Validate(command, effects);
        if (validation.Error is { } validationError)
            return CreateLocationResult.Failed(validationError);

        var request = validation.Value!;
        var settings = request.Settings;

        // Un reintento de red con la misma llave devuelve la ubicación ya creada sin procesar la imagen.
        var existing = await locations.FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return ReplayOrConflict(existing, settings);

        var prepared = images.Prepare(request.Image, ImageRequirements.ForLocation, cancellationToken);
        if (prepared.Error is not null)
            return CreateLocationResult.Failed(prepared.Error);

        await using var scope = await locations.BeginCreationAsync(request.IdempotencyKey, cancellationToken);

        // Otra solicitud con la misma llave pudo terminar mientras esta esperaba su turno.
        var concurrent = await scope.FindExistingAsync(cancellationToken);
        if (concurrent is not null)
            return ReplayOrConflict(concurrent, settings);

        var stored = await images.StoreAsync(
            ImagesFolder, Guid.NewGuid().ToString("N"), prepared.Image!, cancellationToken);
        try
        {
            // La ubicación solo queda disponible cuando existen ambas versiones de la imagen.
            var imageReady = stored.ThumbnailUrl is not null;
            var location = new Location
            {
                Name = settings.Name,
                Description = settings.Description,
                ImageUrl = stored.OriginalUrl,
                ThumbnailUrl = stored.ThumbnailUrl,
                ImageStatus = imageReady ? LocationImageStatus.Ready : LocationImageStatus.Failed,
                IsAvailable = imageReady,
                IdempotencyKey = request.IdempotencyKey,
                CardLimit = settings.CardLimit,
                EnergyMode = settings.EnergyMode,
                EnergyAmount = settings.EnergyAmount,
                EffectType = settings.EffectType,
                EffectParameters = settings.EffectParameters
            };

            await scope.CommitAsync(location, cancellationToken);
            return CreateLocationResult.Created(location);
        }
        catch
        {
            await images.DiscardAsync(stored, CancellationToken.None);
            throw;
        }
    }

    // Misma llave y mismos datos es un reintento válido. Misma llave con datos distintos es un
    // uso incorrecto de la llave y se rechaza con 409. La imagen no se compara porque no se
    // guarda su huella.
    private static CreateLocationResult ReplayOrConflict(Location existing, LocationSettings settings)
    {
        var sameRequest =
            existing.Name == settings.Name
            && existing.Description == settings.Description
            && existing.CardLimit == settings.CardLimit
            && existing.EnergyMode == settings.EnergyMode
            && existing.EnergyAmount == settings.EnergyAmount
            && existing.EffectType == settings.EffectType
            && existing.EffectParameters == settings.EffectParameters;

        return sameRequest
            ? CreateLocationResult.Replayed(existing)
            : CreateLocationResult.Failed(new DomainError(
                DomainErrorKind.Conflict,
                "idempotency-key-conflict",
                "La Idempotency-Key ya se usó con datos distintos. Use una llave nueva para crear otra ubicación.",
                "Idempotency-Key"));
    }
}