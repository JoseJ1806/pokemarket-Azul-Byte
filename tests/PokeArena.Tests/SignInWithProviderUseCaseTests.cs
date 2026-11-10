using Microsoft.Extensions.Logging.Abstractions;
using PokeArena.Api.Modules.Identity.Application;
using PokeArena.Api.Modules.Identity.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Tests;

// PRUEBAS UNITARIAS DEL CASO DE USO SignInWithProviderUseCase.
//
// "Unitarias" quiere decir que NO hay base de datos, ni HTTP, ni Google. Cada puerto
// del caso de uso se reemplaza por un DOBLE DE PRUEBA escrito al final de este archivo:
// FakeIdentityProvider:      responde lo que la prueba le diga (válido, inválido, caído).
// InMemoryAccountRepository: guarda cuentas en una lista en memoria.
// RecordingCollectionGranter: anota cada vez que le piden la colección, y puede fallar.
// FakeTokenIssuer:           devuelve un token de mentira con el Id de la cuenta.
//
// Esto es posible gracias a la arquitectura hexagonal: el caso de uso solo conoce
// interfaces, así que se le puede conectar cualquier implementación.
//
// Cada prueba se nombra Escenario_ResultadoEsperado y sigue tres pasos:
// Arrange (preparar), Act (ejecutar) y Assert (comprobar).
public class SignInWithProviderUseCaseTests
{

    // CONTRATO: el resultado de la entrega llega tal cual hasta quien llamó al login. Es lo
    // que le permite al frontend decidir si muestra la bienvenida o el aviso.
    [Theory]
    [InlineData(InitialCollectionOutcome.Granted)]
    [InlineData(InitialCollectionOutcome.AlreadyGranted)]
    [InlineData(InitialCollectionOutcome.NotAvailable)]
    public async Task SignIn_ReportsTheOutcomeOfTheInitialCollection(InitialCollectionOutcome outcome)
    {
        var context = new TestContext(FakeIdentityProvider.Returning(Ash));
        context.Collection.Outcome = outcome;

        var result = await context.UseCase.ExecuteAsync("token", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(outcome, result.InitialCollection!.Value);
    }

    // Identidad de ejemplo, como si Google ya hubiera validado el token.
    private static readonly ExternalIdentity Ash = new("Google", "google-sub-ash", "ash@pallet.com");

    // CRITERIO DE ÉXITO: en el primer ingreso se crea la cuenta con rol jugador y Elo 0.
    [Fact]
    public async Task FirstSignIn_CreatesAccountWithPlayerRoleAndZeroElo()
    {
        var context = new TestContext(FakeIdentityProvider.Returning(Ash));

        var result = await context.UseCase.ExecuteAsync("token-de-google", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.True(result.IsNewAccount);
        Assert.Equal(IdentityRoles.Player, result.Account!.Role);
        Assert.Equal(PlayerAccount.InitialElo, result.Account.Elo);
        Assert.Equal("Google", result.Account.Provider);
        Assert.Equal("google-sub-ash", result.Account.ProviderSubject);
        Assert.Single(context.Accounts.Stored);
    }

    // CRITERIO DE ÉXITO: en los ingresos posteriores se reutiliza la cuenta existente.
    [Fact]
    public async Task SecondSignIn_ReusesTheSameAccount()
    {
        var context = new TestContext(FakeIdentityProvider.Returning(Ash));

        var first = await context.UseCase.ExecuteAsync("token-1", CancellationToken.None);
        var second = await context.UseCase.ExecuteAsync("token-2", CancellationToken.None);

        Assert.False(second.IsNewAccount);
        Assert.Equal(first.Account!.Id, second.Account!.Id);
        Assert.Single(context.Accounts.Stored);
    }

    // CRITERIO ALTERNO: si el jugador cambia el correo en el proveedor, conserva su cuenta.
    [Fact]
    public async Task SignInAfterEmailChange_KeepsTheSameAccount()
    {
        var provider = FakeIdentityProvider.Returning(Ash);
        var context = new TestContext(provider);
        var first = await context.UseCase.ExecuteAsync("token-1", CancellationToken.None);

        // Mismo "sub" (la misma persona), correo nuevo.
        provider.Result = ExternalIdentityResult.Valid(Ash with { Email = "ash.ketchum@nuevo.com" });
        var second = await context.UseCase.ExecuteAsync("token-2", CancellationToken.None);

        Assert.Equal(first.Account!.Id, second.Account!.Id);
        Assert.Single(context.Accounts.Stored);
    }

    // CRITERIO DE ERROR: un token inválido es rechazado y no se crea ninguna cuenta.
    [Fact]
    public async Task InvalidToken_ReturnsUnauthorizedAndCreatesNoAccount()
    {
        var context = new TestContext(new FakeIdentityProvider
        {
            Result = ExternalIdentityResult.InvalidToken("token falso")
        });

        var result = await context.UseCase.ExecuteAsync("token-falso", CancellationToken.None);

        Assert.Equal(DomainErrorKind.Unauthorized, result.Error!.Kind);
        Assert.Equal(ExternalIdentityResult.InvalidTokenCode, result.Error.Code);
        Assert.Empty(context.Accounts.Stored);
        Assert.Empty(context.Collection.PlayersGranted);
    }

    // CRITERIO DE ERROR: si el proveedor no está disponible, error descriptivo y ninguna
    // cuenta en estado parcial.
    [Fact]
    public async Task ProviderUnavailable_ReturnsUnavailableAndCreatesNoAccount()
    {
        var context = new TestContext(new FakeIdentityProvider
        {
            Result = ExternalIdentityResult.Unavailable("Google no responde")
        });

        var result = await context.UseCase.ExecuteAsync("token", CancellationToken.None);

        Assert.Equal(DomainErrorKind.Unavailable, result.Error!.Kind);
        Assert.Equal(ExternalIdentityResult.UnavailableCode, result.Error.Code);
        Assert.Empty(context.Accounts.Stored);
    }

    // Sin token ni siquiera se le pregunta al proveedor: es un error del cliente (400).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingIdToken_ReturnsValidationErrorWithoutCallingTheProvider(string? idToken)
    {
        var provider = FakeIdentityProvider.Returning(Ash);
        var context = new TestContext(provider);

        var result = await context.UseCase.ExecuteAsync(idToken, CancellationToken.None);

        Assert.Equal(DomainErrorKind.Validation, result.Error!.Kind);
        Assert.Equal("idToken", result.Error.Field);
        Assert.Equal(0, provider.Calls);

        // Un ingreso rechazado no intentó ninguna entrega: no hay resultado.
        Assert.Null(result.InitialCollection);
    }

    // La colección inicial se pide en CADA ingreso (la US de colección necesita poder
    // completar una entrega fallida en el siguiente ingreso).
    [Fact]
    public async Task EverySignIn_AsksForTheInitialCollection()
    {
        var context = new TestContext(FakeIdentityProvider.Returning(Ash));

        var first = await context.UseCase.ExecuteAsync("token-1", CancellationToken.None);
        await context.UseCase.ExecuteAsync("token-2", CancellationToken.None);

        Assert.Equal([first.Account!.Id, first.Account.Id], context.Collection.PlayersGranted);
    }

    // Si la entrega de la colección falla, el jugador igual inicia sesión.
    [Fact]
    public async Task CollectionFailure_DoesNotBlockTheSignIn()
    {
        var context = new TestContext(FakeIdentityProvider.Returning(Ash));
        context.Collection.FailWith = new InvalidOperationException("colección caída");

        var result = await context.UseCase.ExecuteAsync("token", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.AccessToken);

        // Si la entrega falla, el jugador ve el aviso de "no disponible".
        Assert.Equal(InitialCollectionOutcome.NotAvailable, result.InitialCollection!.Value);
    }

    // CRITERIO ALTERNO: dos ingresos simultáneos de una identidad nueva crean una sola
    // cuenta. Aquí se simula que otra solicitud guardó la cuenta justo antes que nosotros:
    // el repositorio rechaza la nuestra (como haría el índice único) y el caso de uso
    // debe terminar usando la cuenta que ganó.
    [Fact]
    public async Task ConcurrentCreation_UsesTheAccountCreatedByTheOtherRequest()
    {
        var context = new TestContext(FakeIdentityProvider.Returning(Ash));
        var winner = PlayerAccount.CreatePlayer(Ash);
        context.Accounts.SimulateRaceWith = winner;

        var result = await context.UseCase.ExecuteAsync("token", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.False(result.IsNewAccount);
        Assert.Equal(winner.Id, result.Account!.Id);
        Assert.Single(context.Accounts.Stored);
    }

    // CRITERIO DE ÉXITO: el sistema entrega credenciales propias para la cuenta.
    [Fact]
    public async Task SuccessfulSignIn_IssuesOwnCredentialForTheAccount()
    {
        var context = new TestContext(FakeIdentityProvider.Returning(Ash));

        var result = await context.UseCase.ExecuteAsync("token", CancellationToken.None);

        Assert.Equal($"token-para-{result.Account!.Id}", result.AccessToken!.Value);
    }

    // Une el caso de uso con sus dobles para no repetir esto en cada prueba.
    private sealed class TestContext
    {
        public InMemoryAccountRepository Accounts { get; } = new();
        public RecordingCollectionGranter Collection { get; } = new();
        public SignInWithProviderUseCase UseCase { get; }

        public TestContext(FakeIdentityProvider provider)
        {
            UseCase = new SignInWithProviderUseCase(
                provider,
                Accounts,
                Collection,
                new FakeTokenIssuer(),
                NullLogger<SignInWithProviderUseCase>.Instance);
        }
    }

    // DOBLE: proveedor de identidad que devuelve lo que la prueba indique.
    private sealed class FakeIdentityProvider : IExternalIdentityProvider
    {
        public ExternalIdentityResult Result { get; set; } = ExternalIdentityResult.InvalidToken("sin configurar");
        public int Calls { get; private set; }

        public static FakeIdentityProvider Returning(ExternalIdentity identity) =>
            new() { Result = ExternalIdentityResult.Valid(identity) };

        public Task<ExternalIdentityResult> ValidateAsync(string idToken, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    // DOBLE: repositorio en memoria. Imita el índice único de PostgreSQL.
    private sealed class InMemoryAccountRepository : IPlayerAccountRepository
    {
        public List<PlayerAccount> Stored { get; } = [];

        // Si se asigna, la próxima AddAsync se comporta como si OTRA solicitud hubiera
        // guardado esta cuenta un instante antes: la guarda y rechaza la nuestra.
        public PlayerAccount? SimulateRaceWith { get; set; }

        public Task<PlayerAccount?> FindByProviderAsync(string provider, string subject, CancellationToken cancellationToken) =>
            Task.FromResult(Stored.SingleOrDefault(a => a.Provider == provider && a.ProviderSubject == subject));

        public Task<PlayerAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Stored.SingleOrDefault(a => a.Id == id));

        public Task<bool> AddAsync(PlayerAccount account, CancellationToken cancellationToken)
        {
            if (SimulateRaceWith is { } winner)
            {
                Stored.Add(winner);
                SimulateRaceWith = null;
            }

            // Igual que el índice único: si ya existe esa persona, se rechaza.
            if (Stored.Any(a => a.Provider == account.Provider && a.ProviderSubject == account.ProviderSubject))
                return Task.FromResult(false);

            Stored.Add(account);
            return Task.FromResult(true);
        }
    }

    // DOBLE: anota a quién se le pidió la colección, devuelve el resultado que se le diga
    // y puede fallar a propósito.
    private sealed class RecordingCollectionGranter : IInitialCollectionGranter
    {
        public List<Guid> PlayersGranted { get; } = [];
        public Exception? FailWith { get; set; }

        // Lo que responde el doble. Por defecto, una entrega exitosa.
        public InitialCollectionOutcome Outcome { get; set; } = InitialCollectionOutcome.Granted;

        public Task<InitialCollectionOutcome> GrantIfMissingAsync(Guid playerId, CancellationToken cancellationToken)
        {
            if (FailWith is not null)
                throw FailWith;
            PlayersGranted.Add(playerId);
            return Task.FromResult(Outcome);
        }
    }

    // DOBLE: emisor de tokens que no firma nada, solo deja ver para quién fue.
    private sealed class FakeTokenIssuer : IAccessTokenIssuer
    {
        public AccessToken Issue(PlayerAccount account) =>
            new($"token-para-{account.Id}", DateTimeOffset.UtcNow.AddMinutes(15));
    }
}
