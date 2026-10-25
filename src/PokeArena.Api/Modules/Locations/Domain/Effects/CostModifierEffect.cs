using System.Text.Json;

namespace PokeArena.Api.Modules.Locations.Domain.Effects;

// Modificador de costo: cambia el costo de las cartas jugadas aquí. El tipo afectado es opcional.
// Ej: {"cantidad":-2,"tipoAfectado":"Agua"}
public sealed class CostModifierEffect(ICardTypeLookup cardTypes) : ILocationEffectType
{
    public const string EffectName = "ModificadorDeCosto";
    public const int MinAmount = -5;
    public const int MaxAmount = 5;

    public string Name => EffectName;

    public EffectValidation Validate(JsonElement parameters)
    {
        if (!EffectJson.TryGetInt(parameters, "cantidad", out var amount))
            return EffectValidation.Invalid("ModificadorDeCosto requiere cantidad, un número entero.");

        if (amount is < MinAmount or > MaxAmount)
            return EffectValidation.Invalid("cantidad debe estar entre -5 y 5.");

        string? affectedType = null;
        if (parameters.TryGetProperty("tipoAfectado", out var property) && property.ValueKind != JsonValueKind.Null)
        {
            affectedType = property.ValueKind == JsonValueKind.String ? property.GetString() : null;
            if (string.IsNullOrWhiteSpace(affectedType) || !cardTypes.Exists(affectedType))
                return EffectValidation.Invalid("tipoAfectado debe ser un tipo de carta existente.");
        }

        return EffectValidation.Valid(JsonSerializer.Serialize(new { cantidad = amount, tipoAfectado = affectedType }));
    }
}