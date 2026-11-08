namespace PokeArena.Api.Modules.Identity.Application;

// RESULTADO de intentar entregar la colección inicial en UN ingreso.
//
// Por qué existe: el login necesita saber qué pasó para avisarle al frontend. Sin esto,
// el frontend no podría distinguir "acaba de recibir sus cartas" (mostrar la bienvenida)
// de "todavía no se puede entregar" (mostrar el aviso de juego no disponible).
public enum InitialCollectionOutcome
{
    // La entrega se hizo AHORA: recibió las 15 cartas y los 1.000 cubos en este ingreso.
    Granted,

    // Ya la había recibido en un ingreso anterior. No se hizo nada.
    AlreadyGranted,

    // No se pudo entregar en este ingreso: hay menos de 15 Comunes disponibles. El jugador
    // ve el aviso de "juego no disponible por el momento" y se reintenta en su siguiente
    // ingreso. El login también reporta este valor si la entrega falla por un error.
    NotAvailable,
}

// PUERTO: ENTREGA DE LA COLECCIÓN INICIAL (frontera entre el módulo Identidad y el de Colección).
//
// Qué es: el único punto de contacto entre Inicio de sesión (esta US) y Recibir
// colección inicial. Identidad solo AVISA "este jugador acaba de ingresar". Qué cartas
// recibe, cuántos cubos y cómo se guardan es responsabilidad completa del otro módulo.
//
// Por qué se llama en CADA ingreso y no solo en el primero:
// La US de colección dice: "si la entrega falla, la cuenta no queda con una entrega
// parcial ... y la entrega se completa en el siguiente ingreso". Para que eso funcione,
// cada ingreso tiene que preguntar. Por eso la operación debe ser IDEMPOTENTE: si el
// jugador ya tiene su colección, no hace nada y responde AlreadyGranted.
//
// Quién lo implementa:
// 1. Hoy, NoOpInitialCollectionGranter (no entrega nada y responde NotAvailable).
// 2. Cuando exista el módulo de colección, su propia clase. Solo hay que cambiar una
//    línea de registro en IdentityModule.
public interface IInitialCollectionGranter
{
    // Entrega la colección inicial al jugador si todavía no la recibió, y dice qué pasó.
    // En el diagrama aparece como "GrantIfMissingAsync(playerId)".
    // playerId es el Guid de PlayerAccount.Id (en la base de datos, tipo uuid).
    Task<InitialCollectionOutcome> GrantIfMissingAsync(Guid playerId, CancellationToken cancellationToken);
}