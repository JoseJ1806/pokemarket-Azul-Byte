using Microsoft.EntityFrameworkCore;
using Npgsql;
using PokeArena.Api.Data;
using PokeArena.Api.Modules.Identity.Application;
using PokeArena.Api.Modules.Identity.Domain;

namespace PokeArena.Api.Modules.Identity.Infrastructure;

// ADAPTADOR DE SALIDA: CUENTAS DE JUGADOR EN POSTGRESQL (módulo Identidad y Acceso).
//
// Implementa el puerto IPlayerAccountRepository con Entity Framework Core. Es el único
// lugar del módulo que sabe que las cuentas viven en la tabla player_accounts.
//
// Cómo se evita crear dos cuentas para la misma persona:
// En PokeArenaDbContext hay un ÍNDICE ÚNICO sobre (Provider, ProviderSubject). Si dos
// solicitudes intentan insertar a la misma persona al mismo tiempo, PostgreSQL acepta la
// primera y rechaza la segunda con el error 23505 (unique_violation). AddAsync atrapa
// ese error puntual y devuelve false, y el caso de uso entonces lee la cuenta ganadora.
// No hace falta un bloqueo manual: la base de datos garantiza la unicidad aunque las
// solicitudes lleguen a instancias distintas de la API.
public sealed class EfPlayerAccountRepository(PokeArenaDbContext db) : IPlayerAccountRepository
{
    // AsNoTracking: solo se lee la cuenta. EF no necesita vigilarla para guardar cambios,
    // y así la consulta es más liviana.
    public Task<PlayerAccount?> FindByProviderAsync(string provider, string subject, CancellationToken cancellationToken) =>
        db.PlayerAccounts.AsNoTracking().SingleOrDefaultAsync(
            account => account.Provider == provider && account.ProviderSubject == subject,
            cancellationToken);

    public Task<PlayerAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.PlayerAccounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == id, cancellationToken);

    public async Task<bool> AddAsync(PlayerAccount account, CancellationToken cancellationToken)
    {
        db.PlayerAccounts.Add(account);
        try
        {
            // INSERT INTO player_accounts ...
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Otra solicitud ya creó la cuenta de esta persona. Se le dice a EF que olvide
            // la cuenta que intentamos insertar, para que no la vuelva a intentar si este
            // mismo DbContext guarda algo más adelante en la misma solicitud.
            db.Entry(account).State = EntityState.Detached;
            return false;
        }
    }

    // ¿El error de EF vino de que PostgreSQL rechazó un valor repetido en un índice único?
    // Cualquier otro error (base caída, columna inexistente) NO se atrapa y sigue su curso.
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
