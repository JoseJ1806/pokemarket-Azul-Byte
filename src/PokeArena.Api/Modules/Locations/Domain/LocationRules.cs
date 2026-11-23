namespace PokeArena.Api.Modules.Locations.Domain;

// REGLAS DE DOMINIO DE LA UBICACIÓN.
// Valores permitidos de la historia Crear ubicación. No dependen de HTTP, EF ni archivos.
public static class LocationRules
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxIdempotencyKeyLength = 200;

    public const int MinCardLimit = 0;
    public const int MaxCardLimit = 5;

    public const string EnergyModeFixed = "Fija";
    public const string EnergyModePerCard = "PorCartas";

    public const int MinEnergy = 0;
    public const int MaxFixedEnergy = 3;
    public const int MaxPerCardEnergy = 2;

    // Una partida tiene 6 turnos.
    public const int TotalTurns = 6;
}

// Mismos valores que CardImageStatus. Más adelante conviene unificar ambos en el módulo Imágenes.
public static class LocationImageStatus
{
    public const string Pending = "Pendiente";
    public const string Ready = "Lista";
    public const string Failed = "Fallida";
}