namespace PokeArena.Api.Modules.Matchmaking.Domain;

// ESTADOS DE UNA BÚSQUEDA DE PARTIDA (módulo Emparejamiento, capa de dominio).
//
// Siguen el modelo de estado "Búsqueda de partida" de la V3 (04. Modelos de estado).
// En la base de datos se guarda el NOMBRE del valor ("Waiting"), no su número.
//
// - Waiting:    "En espera". El jugador busca rival con el deck que indicó.
// - Pairing:    estado interno de "En espera". Ya se eligió rival y se está creando la
//               partida. Para el jugador sigue siendo "En espera" (con el aviso de que se
//               encontró rival). Si la creación falla, las dos búsquedas vuelven a Waiting.
// - MatchFound: "Partida encontrada". Tiene el identificador de la partida.
// - Cancelled:  "Cancelada". El jugador salió de la búsqueda. Nunca se empareja.
// - Expired:    "Expirada". El jugador dejó de consultar la búsqueda. Nunca se empareja.
//
// Waiting y Pairing son las búsquedas ABIERTAS: un jugador solo puede tener una (RN-16).
public enum MatchSearchStatus
{
    Waiting,
    Pairing,
    MatchFound,
    Cancelled,
    Expired
}
