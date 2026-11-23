using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PokeArena.Api.Data;
using PokeArena.Api.Modules.Collection.Application;
using PokeArena.Api.Modules.Collection.Domain;

namespace PokeArena.Api.Modules.Collection.Infrastructure;

// ADAPTADOR DE SALIDA: COLECCIÓN Y CUBOS EN POSTGRESQL (módulo Colección).
//
// Implementa el puerto ICollectionRepository con Entity Framework. Es el único lugar del
// módulo que sabe que los datos viven en card_copies, initial_deliveries, cube_movements
// y cube_balances.
//
// Cómo se garantiza "exactamente una vez" (decisión B): DOS mecanismos que se complementan.
// 1. CANDADO POR JUGADOR (pg_advisory_xact_lock): previene el trabajo duplicado. Dos
//    solicitudes del MISMO jugador se atienden de una en una; los demás jugadores no
//    esperan. El candado vive en la base de datos, no en la memoria de la API, así que
//    funciona con varias instancias (RNF-REN-03). Se suelta solo al terminar la transacción.
// 2. CLAVE PRIMARIA de initial_deliveries: la última barrera. Si algún camino de código
//    se saltara el candado, la base rechazaría la segunda entrega.
public sealed class EfCollectionRepository(PokeArenaDbContext db) : ICollectionRepository
{
    public async Task<IInitialDeliveryScope> BeginDeliveryAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Mismo patrón que los repositorios de Catálogo y Ubicaciones. hashtext convierte
            // el texto en el número que pide el candado. Si dos textos distintos dieran el
            // mismo número, solo esperarían de más: nunca se mezclan datos.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext({0}))",
                new object[] { "initial-delivery:" + playerId },
                cancellationToken);
            return new DeliveryScope(db, transaction, playerId);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<CardCopyCount>> GetCopyCountsAsync(Guid playerId, CancellationToken cancellationToken) =>
        await db.CardCopies.AsNoTracking()
            .Where(copy => copy.PlayerId == playerId)
            .GroupBy(copy => copy.CardId)
            .Select(group => new CardCopyCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

    // Si el jugador no tiene fila de saldo, SingleOrDefault devuelve 0.
    public Task<int> GetCubeBalanceAsync(Guid playerId, CancellationToken cancellationToken) =>
        db.CubeBalances.AsNoTracking()
            .Where(balance => balance.PlayerId == playerId)
            .Select(balance => balance.Balance)
            .SingleOrDefaultAsync(cancellationToken);

    private sealed class DeliveryScope(
        PokeArenaDbContext db,
        IDbContextTransaction transaction,
        Guid playerId) : IInitialDeliveryScope
    {
        // Los movimientos anotados, para calcular el saldo al confirmar.
        private readonly List<CubeMovement> movements = [];

        public Task<bool> HasInitialDeliveryAsync(CancellationToken cancellationToken) =>
            db.InitialDeliveries.AsNoTracking().AnyAsync(delivery => delivery.PlayerId == playerId, cancellationToken);

        public void AddInitialDelivery(InitialDelivery delivery) => db.InitialDeliveries.Add(delivery);

        public void AddCopies(IReadOnlyList<CardCopy> copies) => db.CardCopies.AddRange(copies);

        public void AddCubeMovement(CubeMovement movement)
        {
            db.CubeMovements.Add(movement);
            movements.Add(movement);
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            try
            {
                // INSERT del registro de entrega, de las 15 copias y del movimiento.
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // La clave primaria de initial_deliveries rechazó una segunda entrega.
                throw new InitialDeliveryAlreadyExistsException(playerId, exception);
            }

            // SALDO: único punto del código que lo escribe (decisión E). Se hace con un
            // UPSERT en SQL: si el jugador no tiene fila, la crea; si tiene, le SUMA el monto.
            // Al ser una sola sentencia atómica, dos cambios simultáneos no se pisan, y la
            // restricción de la base (saldo >= 0) rechaza cualquier resultado negativo.
            foreach (var byPlayer in movements.GroupBy(movement => movement.PlayerId))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO cube_balances (\"PlayerId\", \"Balance\", \"UpdatedAt\") VALUES ({0}, {1}, {2}) " +
                    "ON CONFLICT (\"PlayerId\") DO UPDATE SET " +
                    "\"Balance\" = cube_balances.\"Balance\" + EXCLUDED.\"Balance\", " +
                    "\"UpdatedAt\" = EXCLUDED.\"UpdatedAt\"",
                    new object[]
                    {
                        byPlayer.Key,
                        byPlayer.Sum(movement => movement.Amount),
                        byPlayer.Max(movement => movement.CreatedAt),
                    },
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    // ¿El error de EF vino de que PostgreSQL rechazó un valor repetido en una clave única?
    // Cualquier otro error (base caída, restricción de saldo) NO se atrapa y sigue su curso.
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
