using PokeArena.Api.Modules.Collection.Domain;

namespace PokeArena.Api.Modules.Collection.Application;

// Cuántas copias tiene un jugador de una carta. Resultado de agrupar card_copies.
public sealed record CardCopyCount(int CardId, int Copies);

// PUERTO: REPOSITORIO DE LA COLECCIÓN (el "ICollectionRepository" del diagrama SD-04).
//
// Los casos de uso de Colección solo conocen esta interfaz. El adaptador
// EfCollectionRepository la implementa con Entity Framework y PostgreSQL.
public interface ICollectionRepository
{
    // Abre la unidad de trabajo de la entrega inicial para UN jugador: abre una transacción
    // y toma el candado de ese jugador (decisión B). Mientras otra solicitud del MISMO
    // jugador tenga el candado, esta espera. Los demás jugadores no esperan.
    Task<IInitialDeliveryScope> BeginDeliveryAsync(Guid playerId, CancellationToken cancellationToken);

    // Cuántas copias tiene el jugador de cada carta (una fila por carta distinta).
    Task<IReadOnlyList<CardCopyCount>> GetCopyCountsAsync(Guid playerId, CancellationToken cancellationToken);

    // Saldo de cubos del jugador. Es 0 si todavía no tiene fila de saldo.
    Task<int> GetCubeBalanceAsync(Guid playerId, CancellationToken cancellationToken);
}

// ALCANCE DE LA ENTREGA: lo que ocurre dentro de la transacción y el candado.
// Si no se llama a CommitAsync, al liberarlo se cancela TODO (la entrega es todo o nada).
public interface IInitialDeliveryScope : IAsyncDisposable
{
    // ¿Este jugador ya tiene su registro de entrega inicial?
    Task<bool> HasInitialDeliveryAsync(CancellationToken cancellationToken);

    // Las tres operaciones siguientes solo ANOTAN lo que se va a guardar; nada se escribe
    // en la base hasta que se llama a CommitAsync.
    void AddInitialDelivery(InitialDelivery delivery);

    void AddCopies(IReadOnlyList<CardCopy> copies);

    // Anota el movimiento. El saldo se actualiza al confirmar, en la misma transacción.
    void AddCubeMovement(CubeMovement movement);

    // Guarda todo lo anotado y confirma la transacción. Si el jugador ya tenía su
    // entrega registrada, lanza InitialDeliveryAlreadyExistsException.
    Task CommitAsync(CancellationToken cancellationToken);
}
