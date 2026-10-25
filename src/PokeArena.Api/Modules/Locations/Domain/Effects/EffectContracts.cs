using System.Text.Json;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Locations.Domain.Effects;

// Resultado de validar un efecto. Si no hay error, NormalizedParameters es el JSON limpio
// que se guarda (null cuando la ubicación no tiene efecto).
public sealed record EffectValidation(string? NormalizedParameters, DomainError? Error)
{
    public static readonly EffectValidation None = new(null, null);

    public static EffectValidation Valid(string normalizedParameters) => new(normalizedParameters, null);

    public static EffectValidation Invalid(
        string message,
        string field = "effectParameters",
        string code = "invalid-effect-parameters") =>
        new(null, DomainError.Validation(code, message, field));
}

// Cada tipo de efecto del catálogo implementa esta interfaz (abierto/cerrado).
public interface ILocationEffectType
{
    string Name { get; }

    EffectValidation Validate(JsonElement parameters);
}

// Puerto para saber si un tipo de carta existe, sin depender del módulo Catálogo.
public interface ICardTypeLookup
{
    bool Exists(string cardType);
}

internal static class EffectJson
{
    public static bool TryGetInt(JsonElement parameters, string name, out int value)
    {
        value = 0;
        return parameters.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    public static bool TryGetString(JsonElement parameters, string name, out string? value)
    {
        value = null;
        if (!parameters.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString();
        return true;
    }
}