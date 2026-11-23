using PokeArena.Api.Modules.Identity.Domain;

namespace PokeArena.Api.Modules.Identity.Application;

// PUERTO: REPOSITORIO DE CUENTAS DE JUGADOR (módulo Identidad y Acceso, capa de aplicación).
//
// Qué es: el contrato para guardar y buscar cuentas, sin decir DÓNDE se guardan.
// Es el patrón REPOSITORIO: el caso de uso trabaja con objetos PlayerAccount y no ve
// tablas, SQL ni Entity Framework.
//
// Quién lo implementa:
// 1. En producción, EfPlayerAccountRepository (Infrastructure) con PostgreSQL.
// 2. En las pruebas unitarias, un repositorio en memoria (ver SignInWithProviderUseCaseTests).
public interface IPlayerAccountRepository
{
    // Busca la cuenta que corresponde a una persona de un proveedor. Devuelve null si
    // esa persona nunca había ingresado. En el diagrama aparece como
    // "FindByProviderAsync(Provider, Subject)".
    Task<PlayerAccount?> FindByProviderAsync(string provider, string subject, CancellationToken cancellationToken);

    // Busca una cuenta por su Id interno. La usa GET /api/me para devolver los datos
    // del jugador dueño del token.
    Task<PlayerAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    // Intenta guardar una cuenta nueva. En el diagrama aparece como "AddAsync(account)".
    //
    // Devuelve:
    // true:  la cuenta se guardó.
    // false: otra solicitud guardó ANTES una cuenta para la misma persona (mismo
    //        proveedor e identificador). Pasa cuando dos ingresos de una persona nueva
    //        llegan al mismo tiempo. El caso de uso, al recibir false, vuelve a buscar y
    //        usa la cuenta que ganó. Así se cumple el criterio alterno de la US: "dos
    //        inicios de sesión simultáneos de una misma identidad nueva crean exactamente
    //        una cuenta".
    Task<bool> AddAsync(PlayerAccount account, CancellationToken cancellationToken);
}
