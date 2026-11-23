using System.Text.Json;
using PokeArena.Api.Modules.Locations.Domain;

namespace PokeArena.Api.Modules.Locations.Http;

// Contrato JSON de una ubicación. No expone la llave de idempotencia.
// EffectParameters se devuelve como objeto JSON, no como texto.
public sealed record LocationResponse(
    int Id,
    string Name,
    string Description,
    string? ImageUrl,
    string? ThumbnailUrl,
    string ImageStatus,
    bool IsAvailable,
    int CardLimit,
    string EnergyMode,
    int EnergyAmount,
    string? EffectType,
    JsonElement? EffectParameters)
{
    public static LocationResponse From(Location location)
    {
        JsonElement? parameters = null;
        if (!string.IsNullOrWhiteSpace(location.EffectParameters))
        {
            using var document = JsonDocument.Parse(location.EffectParameters);
            parameters = document.RootElement.Clone();
        }

        return new LocationResponse(
            location.Id,
            location.Name,
            location.Description,
            location.ImageUrl,
            location.ThumbnailUrl,
            location.ImageStatus,
            location.IsAvailable,
            location.CardLimit,
            location.EnergyMode,
            location.EnergyAmount,
            location.EffectType,
            parameters);
    }
}