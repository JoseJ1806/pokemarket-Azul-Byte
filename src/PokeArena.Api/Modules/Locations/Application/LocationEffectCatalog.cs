using System.Text.Json;
using PokeArena.Api.Modules.Locations.Domain.Effects;

namespace PokeArena.Api.Modules.Locations.Application;

// CATÁLOGO CERRADO DE TIPOS DE EFECTO.
// Recibe todas las implementaciones de ILocationEffectType por inyección de dependencias.
// Agregar un tipo nuevo no requiere modificar esta clase ni el caso de uso (RNF-ARQ-02).
public sealed class LocationEffectCatalog(IEnumerable<ILocationEffectType> effects)
{
    private readonly IReadOnlyDictionary<string, ILocationEffectType> _effects =
        effects.ToDictionary(effect => effect.Name, StringComparer.Ordinal);

    // effectType vacío significa que la ubicación no tiene efecto.
    public EffectValidation Validate(string? effectType, string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(effectType))
        {
            return string.IsNullOrWhiteSpace(parametersJson)
                ? EffectValidation.None
                : EffectValidation.Invalid(
                    "effectParameters no se permite sin effectType.", "effectParameters");
        }

        if (!_effects.TryGetValue(effectType, out var effect))
        {
            return EffectValidation.Invalid(
                $"effectType no existe. Tipos disponibles: {string.Join(", ", _effects.Keys)}.",
                "effectType",
                "invalid-effect-type");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(parametersJson ?? string.Empty);
        }
        catch (JsonException)
        {
            return EffectValidation.Invalid("effectParameters debe ser un JSON válido.");
        }

        using (document)
        {
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? effect.Validate(document.RootElement)
                : EffectValidation.Invalid("effectParameters debe ser un objeto JSON.");
        }
    }
}