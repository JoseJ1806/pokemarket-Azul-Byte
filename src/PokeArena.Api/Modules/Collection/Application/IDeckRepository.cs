using PokeArena.Api.Modules.Collection.Domain;

namespace PokeArena.Api.Modules.Collection.Application;

// PUERTO: REPOSITORIO DE DECKS (módulo Colección, capa de aplicación).
//
// Los casos de uso de decks solo conocen esta interfaz. El adaptador EfDeckRepository la
// implementa con Entity Framework y PostgreSQL. Mismo estilo que ICollectionRepository.
public interface IDeckRepository
{
    // Abre la unidad de trabajo de la creación de un deck para UN jugador: abre una
    // transacción y toma el candado de ese jugador. Mientras otra solicitud del MISMO
    // jugador tenga el candado, esta espera. Los demás jugadores no esperan.
    Task<IDeckCreationScope> BeginCreationAsync(Guid playerId, CancellationToken cancellationToken);

    // De los identificadores pedidos, devuelve cuáles posee el jugador (al menos una copia).
    // Es UNA sola consulta con el filtro por jugador y carta; nunca se carga la colección
    // completa ni el catálogo.
    Task<IReadOnlyList<int>> GetOwnedCardIdsAsync(
        Guid playerId,
        IReadOnlyCollection<int> cardIds,
        CancellationToken cancellationToken);

    // El deck que el jugador creó con esa llave de idempotencia, o null.
    Task<Deck?> FindByIdempotencyKeyAsync(Guid playerId, string idempotencyKey, CancellationToken cancellationToken);

    // Los decks del jugador, del más antiguo al más reciente.
    Task<IReadOnlyList<Deck>> ListAsync(Guid playerId, CancellationToken cancellationToken);

    // El deck con ese identificador SOLO si es de ese jugador. Si no existe o es de otro,
    // devuelve null: el llamador no puede distinguir los dos casos (decks ajenos son 404).
    Task<Deck?> FindAsync(Guid playerId, Guid deckId, CancellationToken cancellationToken);
}

// ALCANCE DE LA CREACIÓN: lo que ocurre dentro de la transacción y el candado del jugador.
// Es la REGIÓN CRÍTICA de Crear Deck: dentro solo hay operaciones de base de datos.
// Si no se llama a CommitAsync, al liberarlo se cancela todo.
public interface IDeckCreationScope : IAsyncDisposable
{
    // ¿Otra solicitud del jugador ya creó un deck con esta llave? (mientras esta esperaba)
    Task<Deck?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    // ¿El jugador ya tiene un deck con este nombre? La comparación ignora mayúsculas.
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken);

    // Anota el deck y sus 12 cartas. No escribe nada hasta CommitAsync.
    void Add(Deck deck);

    // Guarda y confirma. Si un índice único rechaza el deck, lanza DeckAlreadyExistsException.
    Task CommitAsync(CancellationToken cancellationToken);
}
