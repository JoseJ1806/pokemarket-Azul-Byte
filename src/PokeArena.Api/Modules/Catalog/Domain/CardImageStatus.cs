namespace PokeArena.Api.Modules.Catalog.Domain;

// ESTADOS DE LA IMAGEN DE UNA CARTA (ADR-002).
// Solo una carta con imagen Lista está disponible para los jugadores. Si la versión
// pequeña falla en los 3 intentos, la carta se guarda como Fallida y el administrador
// puede ver ese estado al consultarla.
public static class CardImageStatus
{
    public const string Pending = "Pendiente";
    public const string Ready = "Lista";
    public const string Failed = "Fallida";
}
