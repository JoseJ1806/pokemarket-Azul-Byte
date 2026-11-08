using PokeArena.Api.Modules.Identity.Application;

namespace PokeArena.Api.Modules.Identity.Infrastructure;

// IMPLEMENTACIÓN TEMPORAL del puerto IInitialCollectionGranter. NO ENTREGA NADA.
//
// Responde NotAvailable porque es la verdad: mientras el módulo de colección no exista,
// la colección no se puede entregar. Así el frontend muestra el aviso de "juego no
// disponible" en vez de fingir que se entregó algo.
//
// Cuándo se borra: cuando el módulo de colección tenga su propia implementación del
// puerto. En ese momento se cambia UNA línea en IdentityModule.AddIdentityModule (la que
// registra IInitialCollectionGranter) y este archivo se elimina.
public sealed class NoOpInitialCollectionGranter : IInitialCollectionGranter
{
    public Task<InitialCollectionOutcome> GrantIfMissingAsync(Guid playerId, CancellationToken cancellationToken) =>
        Task.FromResult(InitialCollectionOutcome.NotAvailable);
}