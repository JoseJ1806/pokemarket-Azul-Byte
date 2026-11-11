using System.Text.Json;
using System.Text.Json.Nodes;

namespace PokeArena.Api.Modules.Locations.Application;

// COMPARACIÓN DE PARÁMETROS DE EFECTO.
// PostgreSQL guarda effectParameters como jsonb: al leerlo devuelve el mismo JSON pero con
// otros espacios y otro orden de propiedades, por ejemplo {"turnos": [1, 3]} en lugar de
// {"turnos":[1,3]}. Compararlos como texto hacía que un reintento válido con la misma
// Idempotency-Key se rechazara con 409. Por eso se comparan como JSON.
public static class LocationEffectParameters
{
    public static bool AreEquivalent(string? stored, string? requested)
    {
        var storedIsEmpty = string.IsNullOrWhiteSpace(stored);
        var requestedIsEmpty = string.IsNullOrWhiteSpace(requested);
        if (storedIsEmpty || requestedIsEmpty)
            return storedIsEmpty && requestedIsEmpty;

        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(stored!), JsonNode.Parse(requested!));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
