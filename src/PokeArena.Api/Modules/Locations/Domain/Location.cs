namespace PokeArena.Api.Modules.Locations.Domain;

// ENTIDAD UBICACIÓN (módulo Ubicaciones).
// Una ubicación solo se puede asignar a partidas cuando IsAvailable es true,
// es decir, cuando existen la imagen grande y la versión pequeña.
public class Location
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public string? ThumbnailUrl { get; set; }

    public string ImageStatus { get; set; } = LocationImageStatus.Pending;

    public bool IsAvailable { get; set; }

    // Permite repetir una petición de red sin crear otra ubicación.
    public string IdempotencyKey { get; set; } = string.Empty;

    // Límite de cartas por jugador, de 0 a 5. Con 0 no se pueden jugar cartas aquí.
    public int CardLimit { get; set; }

    // Modalidad de la energía extra: Fija o PorCartas.
    public string EnergyMode { get; set; } = LocationRules.EnergyModeFixed;

    // Monto de energía extra. Fija: de 0 a 3. PorCartas: de 0 a 2.
    public int EnergyAmount { get; set; }

    // Tipo de efecto del catálogo. null significa sin efecto.
    public string? EffectType { get; set; }

    // Parámetros del efecto en JSON, validados según EffectType.
    public string? EffectParameters { get; set; }
}