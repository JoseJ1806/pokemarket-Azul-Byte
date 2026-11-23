namespace PokeArena.Api.Modules.Collection.Domain;

// MOTIVO de un movimiento de cubos: por qué cambió el saldo del jugador.
//
// Se guarda como TEXTO, por la misma razón que CopyOrigin. Los movimientos de apuestas
// y de compras (Fase 2) se agregarán aquí cuando existan sus historias.
public enum CubeMovementReason
{
    // Los 1.000 cubos de la entrega inicial. Es el primer movimiento del historial.
    InitialGrant,
}
