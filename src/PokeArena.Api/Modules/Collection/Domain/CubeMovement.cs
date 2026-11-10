namespace PokeArena.Api.Modules.Collection.Domain;

// MOVIMIENTO DE CUBOS (módulo Colección, capa de dominio).
//
// Qué es: una línea del historial de cubos del jugador. Es la VERDAD del saldo
// (decisión E): el saldo guardado en cube_balances es una copia de conveniencia que
// siempre debe coincidir con la suma de estos movimientos (meta OE-2: 0 diferencias).
//
// Regla de oro: los movimientos SOLO SE AGREGAN. Nunca se editan ni se borran. Si hay un
// error, se corrige con otro movimiento que lo compense, como en contabilidad.
public sealed class CubeMovement
{
    // bigint: es la tabla que más crecerá (cada partida y cada compra agregarán filas).
    public long Id { get; private set; }

    public Guid PlayerId { get; private set; }

    // Con SIGNO: positivo es un ingreso (+1000) y negativo un gasto o pérdida. Nunca es
    // cero: la base de datos lo rechaza con una restricción.
    public int Amount { get; private set; }

    // Por qué ocurrió el movimiento.
    public CubeMovementReason Reason { get; private set; }

    // Cuándo ocurrió (UTC).
    public DateTimeOffset CreatedAt { get; private set; }

    private CubeMovement()
    {
    }

    // MÉTODO DE FÁBRICA del primer movimiento: los cubos de la entrega inicial.
    public static CubeMovement CreateInitialGrant(Guid playerId, DateTimeOffset createdAt) =>
        new()
        {
            PlayerId = playerId,
            Amount = InitialCollectionRules.InitialCubes,
            Reason = CubeMovementReason.InitialGrant,
            CreatedAt = createdAt,
        };
}
