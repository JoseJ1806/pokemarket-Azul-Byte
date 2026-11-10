using PokeArena.Api.Modules.Identity.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Identity.Application;

// RESULTADO DEL INICIO DE SESIÓN.
// Mismo estilo que CreateCardResult: si Error es null, todo salió bien y vienen los datos.
//
// Account:      la cuenta del jugador (nueva o existente).
// AccessToken:  la credencial propia de PokéArena que el cliente debe usar de aquí en adelante.
// IsNewAccount: true si este fue el primer ingreso. El frontend lo usa para mostrar la
//               bienvenida con la colección inicial.
// Error:        el motivo del rechazo, si lo hubo.
// InitialCollection: qué pasó con la entrega de la colección inicial en ESTE ingreso.
//               El frontend lo usa para decidir qué mostrar: la bienvenida con las cartas,
//               nada, o el aviso de "juego no disponible". Es null cuando el ingreso fue
//               rechazado, porque entonces ni siquiera se intentó la entrega.
public sealed record SignInResult(
    PlayerAccount? Account,
    AccessToken? AccessToken,
    bool IsNewAccount,
    InitialCollectionOutcome? InitialCollection,
    DomainError? Error)
{
    public static SignInResult Succeeded(
        PlayerAccount account,
        AccessToken token,
        bool isNewAccount,
        InitialCollectionOutcome initialCollection) =>
        new(account, token, isNewAccount, initialCollection, null);

    public static SignInResult Failed(DomainError error) => new(null, null, false, null, error);
}

// CASO DE USO: INICIAR SESIÓN CON UN PROVEEDOR EXTERNO (módulo Identidad y Acceso).
//
// Es el corazón de la US "Inicio de sesión con proveedor" y sigue paso a paso el
// diagrama de secuencia de la wiki V5. Es un SERVICIO DE APLICACIÓN: coordina a los
// puertos pero no sabe nada de HTTP, de Google ni de PostgreSQL. Por eso se puede
// probar completo con dobles falsos (ver SignInWithProviderUseCaseTests).
//
// Dependencias (todas son PUERTOS, es decir interfaces):
// identityProvider: valida el token externo (GoogleIdentityProvider en producción).
// accounts:         guarda y busca cuentas (EfPlayerAccountRepository en producción).
// collection:       entrega la colección inicial (módulo de otra US).
// tokens:           emite la credencial propia (JwtAccessTokenIssuer en producción).
// logger:           registra problemas que no deben frenar el ingreso.
//
// Orden del flujo:
// 1. Revisa que llegó un token.
// 2. Le pide al proveedor que lo valide. Si no sirve o el proveedor no responde,
//    termina AQUÍ, antes de tocar la base de datos. Por eso nunca queda una cuenta a
//    medio crear (criterio de error de la US).
// 3. Busca la cuenta por proveedor e identificador.
// 4. Si no existe, la crea con rol Player y Elo 0. Si otra solicitud simultánea la
//    creó primero, usa esa.
// 5. Pide la colección inicial (si ya la tiene, el otro módulo no hace nada).
// 6. Emite la credencial propia y devuelve el resultado.
public sealed class SignInWithProviderUseCase(
    IExternalIdentityProvider identityProvider,
    IPlayerAccountRepository accounts,
    IInitialCollectionGranter collection,
    IAccessTokenIssuer tokens,
    ILogger<SignInWithProviderUseCase> logger)
{
    // En el diagrama aparece como "ExecuteAsync(idToken)".
    public async Task<SignInResult> ExecuteAsync(string? idToken, CancellationToken cancellationToken)
    {
        // PASO 1. Sin token no hay nada que validar. Es un error del cliente (400), no
        // un problema de identidad, y por eso ni siquiera se consulta al proveedor.
        if (string.IsNullOrWhiteSpace(idToken))
            return SignInResult.Failed(
                DomainError.Validation("required-field", "idToken es obligatorio.", "idToken"));

        // PASO 2. Validar el token con el proveedor externo.
        var validation = await identityProvider.ValidateAsync(idToken, cancellationToken);
        if (validation.Error is { } providerError)
            // Token inválido (401) o proveedor caído (503). Todavía no se tocó la base de
            // datos, así que es imposible que quede una cuenta creada a medias.
            return SignInResult.Failed(providerError);

        var identity = validation.Identity!;

        // PASO 3. ¿Esta persona ya tiene cuenta? Se busca por proveedor + identificador,
        // NUNCA por correo. Si la persona cambió su correo en Google, el identificador
        // ("sub") sigue siendo el mismo y se encuentra la misma cuenta.
        var account = await accounts.FindByProviderAsync(identity.Provider, identity.Subject, cancellationToken);
        var isNewAccount = false;

        // PASO 4. Primer ingreso: crear la cuenta.
        if (account is null)
        {
            // El método de fábrica asigna rol Player y Elo 0.
            var newAccount = PlayerAccount.CreatePlayer(identity);

            if (await accounts.AddAsync(newAccount, cancellationToken))
            {
                // Esta solicitud fue la que creó la cuenta.
                account = newAccount;
                isNewAccount = true;
            }
            else
            {
                // CARRERA ENTRE SOLICITUDES. Otra solicitud de la misma persona guardó su
                // cuenta entre nuestra búsqueda (paso 3) y nuestro guardado. El índice
                // único de la base de datos rechazó la nuestra. Se busca de nuevo y se usa
                // la que ganó, así la persona termina con exactamente una cuenta.
                account = await accounts.FindByProviderAsync(identity.Provider, identity.Subject, cancellationToken)
                    ?? throw new InvalidOperationException(
                        "El índice único rechazó la cuenta, pero no se encontró la cuenta existente.");
            }
        }

        // PASO 5. Pedir la colección inicial en CADA ingreso (ver IInitialCollectionGranter
        // para el porqué). Si falla, el jugador igual puede entrar: se registra el error y
        // la entrega se reintenta en el siguiente ingreso, como pide la US de colección.
        //
        // Si falla, el resultado que se reporta es NotAvailable: para el jugador es lo mismo que
        // "todavía no se puede entregar", y el detalle del error queda solo en el log.
        var initialCollection = InitialCollectionOutcome.NotAvailable;
        try
        {
            initialCollection = await collection.GrantIfMissingAsync(account.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // OperationCanceledException se deja pasar porque significa que el cliente
            // cerró la conexión, y ahí no tiene sentido seguir.
            logger.LogError(
                exception,
                "No se pudo entregar la colección inicial al jugador {PlayerId}. Se reintentará en su siguiente ingreso.",
                account.Id);
        }

        // PASO 6. Emitir la credencial propia (JWT de PokéArena con expiración corta).
        var accessToken = tokens.Issue(account);
        return SignInResult.Succeeded(account, accessToken, isNewAccount, initialCollection);
    }
}
