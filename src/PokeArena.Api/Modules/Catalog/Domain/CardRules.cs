namespace PokeArena.Api.Modules.Catalog.Domain;

// REGLAS DE DOMINIO DE LA CARTA.
// Valores permitidos de la historia Crear carta. No dependen de HTTP, EF ni archivos.
// Nota: las entidades Card y Ability siguen en PokeArena.Api.Models porque las comparte
// la búsqueda y las migraciones. Se moverán aquí cuando se migre Buscar carta.
public static class CardRules
{
    public const int MaxNameLength = 200;
    public const int MaxIdempotencyKeyLength = 200;

    public const int MinCost = 0;
    public const int MaxCost = 10;

    public const int MinPower = -20;
    public const int MaxPower = 20;

    // Habilidad que no tiene efecto. Existe desde la primera migración.
    public const string NoAbilityCode = "HAB-000";

    // Abreviatura de cada tipo para el código PKA-<TIPO>-<NNN>.
    public static readonly IReadOnlyDictionary<string, string> TypePrefixes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Fuego"] = "FUE",
            ["Agua"] = "AGU",
            ["Planta"] = "PLA",
            ["Eléctrico"] = "ELE",
            ["Psíquico"] = "PSI",
            ["Lucha"] = "LUC"
        };

    public static readonly IReadOnlySet<string> Rarities =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Común", "Poco común", "Rara", "Legendaria"
        };

    // Indica si un costo está dentro del rango permitido (0 a 10).
    // Es la ÚNICA definición de esta regla: Crear carta y Buscar carta la usan
    // las dos, así no pueden contradecirse.
    public static bool IsValidCost(int cost) => cost is >= MinCost and <= MaxCost;

    // Indica si un poder está dentro del rango permitido (-20 a 20).
    // "is >= a and <= b" significa "mayor o igual que a Y menor o igual que b".
    // Los poderes negativos son válidos: una carta puede restar poder a su ubicación.
    public static bool IsValidPower(int power) => power is >= MinPower and <= MaxPower;
}
