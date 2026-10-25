using PokeArena.Api.Modules.Locations.Domain;

namespace PokeArena.Api.Modules.Locations.Application;

// PUERTO RepositorioUbicaciones del diagrama de arquitectura lógica.
// Los casos de uso de Ubicaciones solo conocen esta interfaz. El adaptador
// EfLocationRepository la implementa con Entity Framework y PostgreSQL.
public interface ILocationRepository
{
    Task<Location?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    // Abre una unidad de trabajo exclusiva para una llave de idempotencia.
    Task<ILocationCreationScope> BeginCreationAsync(string idempotencyKey, CancellationToken cancellationToken);
}

public interface ILocationCreationScope : IAsyncDisposable
{
    Task<Location?> FindExistingAsync(CancellationToken cancellationToken);

    // Guarda la ubicación y confirma la unidad de trabajo. Si no se llama, se descarta todo.
    Task CommitAsync(Location location, CancellationToken cancellationToken);
}