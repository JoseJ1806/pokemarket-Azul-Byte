using PokeArena.Api.Modules.Identity.Application;

namespace PokeArena.Api.Modules.Identity.Infrastructure;

// IMPLEMENTACIÓN TEMPORAL del puerto IInitialCollectionGranter. NO HACE NADA.
//
// Por qué existe: Inicio de sesión necesita llamar a la entrega de la colección inicial,
// pero esa funcionalidad es otra US y la desarrolla otra persona del equipo. Para que
// esta US funcione y se pueda probar sin esperar, se registra esta versión vacía.
//
// Cuándo se borra: cuando el módulo de colección tenga su propia implementación del
// puerto. En ese momento se cambia UNA línea en IdentityModule.AddIdentityModule (la que
// registra IInitialCollectionGranter) y este archivo se elimina.
public sealed class NoOpInitialCollectionGranter : IInitialCollectionGranter
{
    public Task GrantIfMissingAsync(Guid playerId, CancellationToken cancellationToken) => Task.CompletedTask;
}
