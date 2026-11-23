namespace PokeArena.Api.Modules.Collection.Domain;

// ORIGEN de una copia de carta: cómo llegó a la colección del jugador.
//
// Se guarda como TEXTO ("Initial") y no como número, igual que el estado de las
// imágenes: así se lee bien directamente en la base de datos y no depende del orden
// de los valores del enum.
public enum CopyOrigin
{
    // Recibida en la entrega de la colección inicial (US Recibir colección inicial).
    // Cuando existan las recompensas por partida se agregará aquí el valor Reward.
    Initial,
}
