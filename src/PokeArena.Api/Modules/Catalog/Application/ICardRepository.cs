using PokeArena.Api.Models;

namespace PokeArena.Api.Modules.Catalog.Application;

// PUERTO RepositorioCartas del diagrama de arquitectura lógica.
// Los casos de uso del catálogo solo conocen esta interfaz. El adaptador
// EfCardRepository la implementa con Entity Framework y PostgreSQL.
public interface ICardRepository
{
    Task<Card?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<Ability?> FindAbilityAsync(string code, CancellationToken cancellationToken);

    // Abre una unidad de trabajo exclusiva para una llave de idempotencia y un tipo de carta.
    // Dos creaciones con la misma llave o del mismo tipo se atienden una a la vez.
    Task<ICardCreationScope> BeginCreationAsync(
        string idempotencyKey,
        string typePrefix,
        CancellationToken cancellationToken);
}

public interface ICardCreationScope : IAsyncDisposable
{
    Task<Card?> FindExistingAsync(CancellationToken cancellationToken);

    Task<string> ReserveNextCodeAsync(CancellationToken cancellationToken);

    Task<bool> EvolutionCreatesCycleAsync(
        string newCardCode,
        string targetCardCode,
        CancellationToken cancellationToken);

    // Guarda la carta y confirma la unidad de trabajo. Si no se llama, se descarta todo.
    Task CommitAsync(Card card, CancellationToken cancellationToken);
}
