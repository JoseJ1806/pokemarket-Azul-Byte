using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Modules.Locations.Domain;
using PokeArena.Api.Modules.Locations.Domain.Effects;
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
// Orden del flujo:
// 1. Valida los campos, la configuración, el efecto y la llave de idempotencia.
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

    // Datos ya validados y normalizados. Sirven para crear y para comparar reintentos.
    private sealed record Settings(
        string Name,
        string Description,
        int CardLimit,
        string EnergyMode,
        int EnergyAmount,
        string? EffectType,
        string? EffectParameters);

    public async Task<CreateLocationResult> ExecuteAsync(
        CreateLocationCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return Invalid("missing-idempotency-key", "Idempotency-Key es obligatorio.", "Idempotency-Key");

        if (command.IdempotencyKey.Length > LocationRules.MaxIdempotencyKeyLength)
            return Invalid(
                "invalid-field",
                $"Idempotency-Key no puede superar {LocationRules.MaxIdempotencyKeyLength} caracteres.",
                "Idempotency-Key");

        var name = command.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Invalid("required-field", "name es obligatorio.", "name");

        if (name.Length > LocationRules.MaxNameLength)
            return Invalid("invalid-field", $"name no puede superar {LocationRules.MaxNameLength} caracteres.", "name");

        var description = command.Description?.Trim();
        if (string.IsNullOrWhiteSpace(description))
            return Invalid("required-field", "description es obligatorio.", "description");

        if (description.Length > LocationRules.MaxDescriptionLength)
            return Invalid(
                "invalid-field",
                $"description no puede superar {LocationRules.MaxDescriptionLength} caracteres.",
                "description");

        if (command.CardLimit is null)
            return Invalid("required-field", "cardLimit es obligatorio.", "cardLimit");

        if (string.IsNullOrWhiteSpace(command.EnergyMode))
            return Invalid("required-field", "energyMode es obligatorio.", "energyMode");

        if (command.EnergyAmount is null)
            return Invalid("required-field", "energyAmount es obligatorio.", "energyAmount");

        var settingsError = LocationSettingsRules.Validate(
            command.CardLimit.Value, command.EnergyMode, command.EnergyAmount.Value);
        if (settingsError is not null)
            return CreateLocationResult.Failed(settingsError);

        var effectType = string.IsNullOrWhiteSpace(command.EffectType) ? null : command.EffectType.Trim();
        var effect = effects.Validate(effectType, command.EffectParameters);
        if (effect.Error is not null)
            return CreateLocationResult.Failed(effect.Error);

        if (command.Image is null)
            return Invalid("required-field", "image es obligatorio.", "image");

        var settings = new Settings(
            name,
            description,
            command.CardLimit.Value,
            command.EnergyMode,
            command.EnergyAmount.Value,
            effectType,
            effect.NormalizedParameters);

        // Un reintento de red con la misma llave devuelve la ubicación ya creada sin procesar la imagen.
        var existing = await locations.FindByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return ReplayOrConflict(existing, settings);

        var prepared = images.Prepare(command.Image, ImageRequirements.ForLocation, cancellationToken);
        if (prepared.Error is not null)
            return CreateLocationResult.Failed(prepared.Error);

        await using var scope = await locations.BeginCreationAsync(command.IdempotencyKey, cancellationToken);

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
                IdempotencyKey = command.IdempotencyKey,
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
    private static CreateLocationResult ReplayOrConflict(Location existing, Settings settings)
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

    private static CreateLocationResult Invalid(string code, string message, string field) =>
        CreateLocationResult.Failed(DomainError.Validation(code, message, field));
}