using System.Text.Json;

namespace PokeArena.Api.Modules.Locations.Domain.Effects;

// Bonificación por tipo: suma o resta poder a las cartas de un tipo. Ej: {"tipo":"Fuego","cantidad":3}
public sealed class TypeBonusEffect(ICardTypeLookup cardTypes) : ILocationEffectType
{
    public const string EffectName = "BonificacionPorTipo";
    public const int MinAmount = -5;
    public const int MaxAmount = 5;

    public string Name => EffectName;

    public EffectValidation Validate(JsonElement parameters)
    {
        if (!EffectJson.TryGetString(parameters, "tipo", out var cardType) || string.IsNullOrWhiteSpace(cardType))
            return EffectValidation.Invalid("BonificacionPorTipo requiere el parámetro tipo.");

        if (!cardTypes.Exists(cardType))
            return EffectValidation.Invalid("tipo debe ser un tipo de carta existente.");

        if (!EffectJson.TryGetInt(parameters, "cantidad", out var amount))
            return EffectValidation.Invalid("BonificacionPorTipo requiere cantidad, un número entero.");

        if (amount is < MinAmount or > MaxAmount)
            return EffectValidation.Invalid("cantidad debe estar entre -5 y 5.");

        return EffectValidation.Valid(JsonSerializer.Serialize(new { tipo = cardType, cantidad = amount }));
    }
}