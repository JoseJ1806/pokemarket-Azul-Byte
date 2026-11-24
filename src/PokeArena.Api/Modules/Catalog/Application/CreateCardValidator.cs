using PokeArena.Api.Models;
using PokeArena.Api.Modules.Catalog.Domain;
using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Catalog.Application;

// Datos de CreateCardCommand ya validados y normalizados.
public sealed record ValidCardRequest(
    string IdempotencyKey,
    string Name,
    int Cost,
    int Power,
    string Type,
    string TypePrefix,
    string Rarity,
    string AbilityCode,
    ImageUpload Image);

// VALIDACIONES DE CREAR CARTA, separadas del flujo de CreateCardUseCase.
// Validate revisa los campos del comando sin tocar la base de datos y devuelve el primer
// error en el mismo orden de siempre. ValidateAbility revisa la habilidad ya encontrada.
// Los valores permitidos viven en CardRules (dominio).
public static class CreateCardValidator
{
    public static Validated<ValidCardRequest> Validate(CreateCardCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return Invalid("missing-idempotency-key", "Idempotency-Key es obligatorio.", "Idempotency-Key");

        if (command.IdempotencyKey.Length > CardRules.MaxIdempotencyKeyLength)
            return Invalid("invalid-field", $"Idempotency-Key no puede superar {CardRules.MaxIdempotencyKeyLength} caracteres.", "Idempotency-Key");

        if (string.IsNullOrWhiteSpace(command.Name))
            return Invalid("required-field", "name es obligatorio.", "name");

        var name = command.Name.Trim();
        if (name.Length > CardRules.MaxNameLength)
            return Invalid("invalid-field", $"name no puede superar {CardRules.MaxNameLength} caracteres.", "name");

        if (command.Cost is < CardRules.MinCost or > CardRules.MaxCost)
            return Invalid("invalid-field", "cost debe estar entre 0 y 10.", "cost");

        if (command.Power is < CardRules.MinPower or > CardRules.MaxPower)
            return Invalid("invalid-field", "power debe estar entre -20 y 20.", "power");

        if (!CardRules.TypePrefixes.TryGetValue(command.Type ?? string.Empty, out var typePrefix))
            return Invalid("invalid-field", "type debe ser Fuego, Agua, Planta, Eléctrico, Psíquico o Lucha.", "type");

        if (!CardRules.Rarities.Contains(command.Rarity ?? string.Empty))
            return Invalid("invalid-field", "rarity debe ser Común, Poco común, Rara o Legendaria.", "rarity");

        if (command.Image is null)
            return Invalid("required-field", "image es obligatorio.", "image");

        var abilityCode = command.AbilityCode?.Trim();
        if (string.IsNullOrWhiteSpace(abilityCode))
            return Invalid("required-field", "abilityCode es obligatorio, seleccione HAB-000 si no tiene habilidad.", "abilityCode");

        return Validated<ValidCardRequest>.Valid(new ValidCardRequest(
            command.IdempotencyKey,
            name,
            command.Cost,
            command.Power,
            command.Type!,
            typePrefix,
            command.Rarity!,
            abilityCode,
            command.Image));
    }

    // Una habilidad de evolución debe apuntar a una carta destino.
    // El ciclo entre cartas se revisa aparte porque necesita la base de datos.
    public static DomainError? ValidateAbility(Ability ability) =>
        ability.IsEvolution && string.IsNullOrWhiteSpace(ability.TargetCardCode)
            ? DomainError.Validation(
                "invalid-evolution", "La habilidad de evolución debe tener una carta destino.", "abilityCode")
            : null;

    private static Validated<ValidCardRequest> Invalid(string code, string message, string field) =>
        Validated<ValidCardRequest>.Invalid(code, message, field);
}
