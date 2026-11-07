namespace PokeArena.Api.Modules.Identity.Application;

// PUERTO: ENTREGA DE LA COLECCIÓN INICIAL (frontera entre el módulo Identidad y el de Colección).
//
// Qué es: el único punto de contacto entre Inicio de sesión (esta US) y Recibir
// colección inicial (la US de otra persona del equipo). Identidad solo AVISA "este
// jugador acaba de ingresar". Qué cartas recibe, cuántos cubos y cómo se guardan es
// responsabilidad completa del otro módulo.
//
// Por qué se llama en CADA ingreso y no solo en el primero:
// La US de colección dice: "si la entrega falla, la cuenta no queda con una entrega
// parcial ... y la entrega se completa en el siguiente ingreso". Para que eso funcione,
// cada ingreso tiene que preguntar. Por eso la operación debe ser IDEMPOTENTE: si el
// jugador ya tiene su colección, no hace nada. Llamarla una vez o cien veces deja el
// mismo resultado.
//
// Quién lo implementa:
// 1. Hoy, NoOpInitialCollectionGranter (no hace nada), para que Inicio de sesión
//    funcione sin esperar a la otra US.
// 2. Cuando exista el módulo de colección, su propia clase. Solo hay que cambiar una
//    línea de registro en IdentityModule.
public interface IInitialCollectionGranter
{
    // Entrega la colección inicial al jugador si todavía no la recibió.
    // En el diagrama aparece como "GrantIfMissingAsync(account.Id)".
    // playerId es el Guid de PlayerAccount.Id (en la base de datos, tipo uuid).
    Task GrantIfMissingAsync(Guid playerId, CancellationToken cancellationToken);
}
