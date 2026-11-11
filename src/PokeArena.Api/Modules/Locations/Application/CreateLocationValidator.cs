using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Modules.Locations.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Locations.Application;

// Datos de la ubicación ya validados y normalizados. Sirven para crear y para comparar reintentos.
public sealed record LocationSettings(
    string Name,
    string Description,
    int CardLimit,
    string EnergyMode,
    int EnergyAmount,
    string? EffectType,
    string? EffectParameters);

public sealed record ValidLocationRequest(string IdempotencyKey, LocationSettings Settings, ImageUpload Image);

// VALIDACIONES DE CREAR UBICACIÓN, separadas del flujo de CreateLocationUseCase.
// Revisa los campos del comando sin tocar la base de datos y devuelve el primer error
// en el mismo orden de siempre. Los valores permitidos viven en LocationRules y
// LocationSettingsRules (dominio), y los tipos de efecto en LocationEffectCatalog.
public static class CreateLocationValidator
{
    public static Validated<ValidLocationRequest> Validate(
        CreateLocationCommand command,
        LocationEffectCatalog effects)
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
            return Validated<ValidLocationRequest>.Invalid(settingsError);

        var effectType = string.IsNullOrWhiteSpace(command.EffectType) ? null : command.EffectType.Trim();
        var effect = effects.Validate(effectType, command.EffectParameters);
        if (effect.Error is not null)
            return Validated<ValidLocationRequest>.Invalid(effect.Error);

        if (command.Image is null)
            return Invalid("required-field", "image es obligatorio.", "image");

        var settings = new LocationSettings(
            name,
            description,
            command.CardLimit.Value,
            command.EnergyMode,
            command.EnergyAmount.Value,
            effectType,
            effect.NormalizedParameters);

        return Validated<ValidLocationRequest>.Valid(
            new ValidLocationRequest(command.IdempotencyKey, settings, command.Image));
    }

    private static Validated<ValidLocationRequest> Invalid(string code, string message, string field) =>
        Validated<ValidLocationRequest>.Invalid(code, message, field);
}
