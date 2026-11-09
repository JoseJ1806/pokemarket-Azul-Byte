namespace PokeArena.Api.Modules.Collection.Domain;

// REGISTRO DE ENTREGA INICIAL (módulo Colección, capa de dominio).
//
// Qué es: una fila que dice "a este jugador ya se le entregó su colección inicial".
//
// Por qué existe: es la BARRERA de "exactamente una vez" (decisión B). Su clave primaria
// es el PlayerId, así que la base de datos es incapaz de guardar dos entregas del mismo
// jugador, aunque dos primeros ingresos lleguen al mismo tiempo a instancias distintas.
//
// Importante: la tabla de copias NO puede cumplir este papel, porque una operación del
// tipo "si ya existe, súmale uno" ocultaría la doble entrega en vez de rechazarla.
public sealed class InitialDelivery
{
    // Clave primaria: una sola fila por jugador.
    public Guid PlayerId { get; private set; }

    // Cuándo se hizo la entrega (UTC). Sirve para auditar.
    public DateTimeOffset DeliveredAt { get; private set; }

    private InitialDelivery()
    {
    }

    public static InitialDelivery Create(Guid playerId, DateTimeOffset deliveredAt) =>
        new() { PlayerId = playerId, DeliveredAt = deliveredAt };
}
