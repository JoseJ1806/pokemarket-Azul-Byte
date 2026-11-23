namespace PokeArena.Api.Modules.Collection.Application;

// ERROR: la base de datos rechazó la entrega porque ese jugador YA tiene su registro.
//
// Cuándo ocurre: en el caso extremo en que dos solicitudes del mismo jugador lograron
// pasar el candado. La clave primaria de initial_deliveries es la última barrera: acepta
// una y rechaza la otra. El repositorio traduce el error técnico de PostgreSQL a esta
// excepción, y el caso de uso la interpreta como "ya estaba entregada" (AlreadyGranted).
public sealed class InitialDeliveryAlreadyExistsException(Guid playerId, Exception inner)
    : Exception($"El jugador {playerId} ya tiene su entrega inicial registrada.", inner)
{
    public Guid PlayerId { get; } = playerId;
}
