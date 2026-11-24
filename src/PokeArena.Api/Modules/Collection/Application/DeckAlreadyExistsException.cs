namespace PokeArena.Api.Modules.Collection.Application;

// ERROR: la base de datos rechazó el deck porque ya existe uno con la misma llave de
// idempotencia o con el mismo nombre para ese jugador.
//
// Cuándo ocurre: casi nunca, porque el candado por jugador ya serializa las creaciones.
// Es la última barrera, igual que la clave primaria de initial_deliveries: si algún camino
// de código se saltara el candado, los índices únicos de la tabla decks rechazarían el
// duplicado. El repositorio traduce el error técnico de PostgreSQL a esta excepción.
//
// ConstraintName es el nombre del índice que se violó; permite distinguir si chocó la
// llave de idempotencia o el nombre.
public sealed class DeckAlreadyExistsException(Guid playerId, string? constraintName, Exception inner)
    : Exception($"El jugador {playerId} ya tiene un deck que choca con este ({constraintName}).", inner)
{
    public Guid PlayerId { get; } = playerId;

    public string? ConstraintName { get; } = constraintName;
}
