using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PokeArena.Api.Data;
using PokeArena.Api.Modules.Locations.Application;
using PokeArena.Api.Modules.Locations.Domain;

namespace PokeArena.Api.Modules.Locations.Infrastructure;

// ADAPTADOR RepositorioUbicaciones.
// Implementa el puerto ILocationRepository con Entity Framework y PostgreSQL. Un bloqueo
// consultivo (advisory lock) hace que una llave de idempotencia se atienda de una en una,
// aunque las solicitudes lleguen a instancias distintas de la API.
public sealed class EfLocationRepository(PokeArenaDbContext db) : ILocationRepository
{
    public Task<Location?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        db.Locations.AsNoTracking()
            .SingleOrDefaultAsync(location => location.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<ILocationCreationScope> BeginCreationAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext({0}))",
                new object[] { "location-idempotency:" + idempotencyKey },
                cancellationToken);

            return new CreationScope(db, transaction, idempotencyKey);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class CreationScope(
        PokeArenaDbContext db,
        IDbContextTransaction transaction,
        string idempotencyKey) : ILocationCreationScope
    {
        public Task<Location?> FindExistingAsync(CancellationToken cancellationToken)
        {
            var key = idempotencyKey;
            return db.Locations.AsNoTracking()
                .SingleOrDefaultAsync(location => location.IdempotencyKey == key, cancellationToken);
        }

        public async Task CommitAsync(Location location, CancellationToken cancellationToken)
        {
            db.Locations.Add(location);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}