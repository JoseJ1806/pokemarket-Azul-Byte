namespace PokeArena.Api.Modules.Collection.Domain;

// SALDO ACTUAL DE CUBOS de un jugador (módulo Colección, capa de dominio).
//
// Qué es: una copia guardada del saldo, para leerlo rápido sin sumar todo el historial.
// La verdad sigue siendo la suma de CubeMovement.Amount (decisión E).
//
// Cómo se escribe: SOLO por un único punto del código, EfCollectionRepository, en la misma
// transacción que el movimiento que lo cambia. Así no puede desfasarse por una falla a
// la mitad. La base de datos además rechaza saldos negativos (RNF-INT-03).
public sealed class CubeBalance
{
    // Clave primaria: una sola fila por jugador.
    public Guid PlayerId { get; private set; }

    // Cubos que tiene ahora. La restricción CK_cube_balances_Balance_NotNegative en la
    // base de datos garantiza que nunca sea menor que 0.
    public int Balance { get; private set; }

    // Cuándo cambió por última vez (UTC).
    public DateTimeOffset UpdatedAt { get; private set; }

    private CubeBalance()
    {
    }
}
