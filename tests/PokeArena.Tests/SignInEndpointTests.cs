using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PokeArena.Api.Data;
using PokeArena.Api.Modules.Identity.Application;
using PokeArena.Api.Modules.Identity.Domain;

namespace PokeArena.Tests;

// PRUEBAS DE INTEGRACIÓN DE POST /api/auth/google Y GET /api/me.
//
// A diferencia de las unitarias, aquí corre la API COMPLETA en memoria (Program.cs,
// IdentityModule, Entity Framework y PostgreSQL de pruebas) y se le hacen solicitudes
// HTTP reales. Lo único que se reemplaza es Google, por FakeGoogleProvider (al final del
// archivo), porque no se le pueden pedir tokens reales a Google en una prueba.
//
// Diferencia importante con CardsApiFactory: esta factoría NO reemplaza la autenticación
// JWT por el TestAuthHandler. Aquí se usa la validación real, porque lo que se quiere
// demostrar es justamente que la API acepta las credenciales que ella emite y rechaza
// las demás (RNF-SEG-01).
[Collection("Auth API")]
public class SignInEndpointTests(AuthApiFactory factory)
{
    // CRITERIO DE ÉXITO: token válido → credenciales propias con expiración corta,
    // cuenta nueva con rol jugador y Elo 0.
    [Fact]
    public async Task ValidGoogleToken_Returns200WithOwnCredential()
    {
        var subject = NewSubject();

        var response = await SignInAsync(FakeGoogleProvider.ValidToken(subject, "misty@cerulean.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.True(body.GetProperty("isNewAccount").GetBoolean());
        Assert.Equal(IdentityRoles.Player, body.GetProperty("player").GetProperty("role").GetString());
        Assert.Equal(0, body.GetProperty("player").GetProperty("elo").GetInt32());

        // Expira en unos 15 minutos (se deja un margen para la lentitud de la prueba).
        var expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.5));
    }

    // CRITERIO DE ÉXITO: ingresos posteriores reutilizan la cuenta.
    [Fact]
    public async Task SecondSignIn_ReturnsTheSameAccount()
    {
        var subject = NewSubject();
        var first = await ReadJsonAsync(await SignInAsync(FakeGoogleProvider.ValidToken(subject)));
        var second = await ReadJsonAsync(await SignInAsync(FakeGoogleProvider.ValidToken(subject)));

        Assert.False(second.GetProperty("isNewAccount").GetBoolean());
        Assert.Equal(
            first.GetProperty("player").GetProperty("id").GetGuid(),
            second.GetProperty("player").GetProperty("id").GetGuid());
        Assert.Equal(1, await factory.CountAccountsAsync(subject));
    }

    // CRITERIO DE ÉXITO: las solicitudes posteriores se autorizan con la credencial de
    // PokéArena. GET /api/me la acepta y devuelve la misma cuenta.
    [Fact]
    public async Task OwnCredential_IsAcceptedByTheApi()
    {
        var signIn = await ReadJsonAsync(await SignInAsync(FakeGoogleProvider.ValidToken(NewSubject())));
        var accessToken = signIn.GetProperty("accessToken").GetString()!;

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var me = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var player = await ReadJsonAsync(me);
        Assert.Equal(signIn.GetProperty("player").GetProperty("id").GetGuid(), player.GetProperty("id").GetGuid());
    }

    // RNF-SEG-03: "no sé quién eres" (401) y "no tienes permiso" (403) son distinguibles.
    // Un jugador está autenticado, pero no es administrador: buscar cartas da 403, no 401.
    [Fact]
    public async Task PlayerCredential_OnAdminEndpoint_Returns403()
    {
        var signIn = await ReadJsonAsync(await SignInAsync(FakeGoogleProvider.ValidToken(NewSubject())));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", signIn.GetProperty("accessToken").GetString());
        var response = await client.GetAsync("/api/cards");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // CRITERIO DE ERROR: token rechazado → 401 con ProblemDetails y ninguna cuenta creada.
    [Fact]
    public async Task InvalidGoogleToken_Returns401ProblemAndCreatesNoAccount()
    {
        var response = await SignInAsync(FakeGoogleProvider.InvalidToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal(ExternalIdentityResult.InvalidTokenCode, problem.GetProperty("code").GetString());
        Assert.Equal(0, await factory.CountAllAccountsWithEmailAsync(FakeGoogleProvider.InvalidMarkerEmail));
    }

    // CRITERIO DE ERROR: proveedor no disponible → 503 descriptivo y ninguna cuenta.
    [Fact]
    public async Task GoogleUnavailable_Returns503ProblemAndCreatesNoAccount()
    {
        var response = await SignInAsync(FakeGoogleProvider.UnavailableToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal(ExternalIdentityResult.UnavailableCode, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    // Sin idToken en el cuerpo: 400 indicando el campo.
    [Fact]
    public async Task MissingIdToken_Returns400()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/google", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("idToken", problem.GetProperty("field").GetString());
    }

    // CRITERIO ALTERNO: dos (o más) inicios de sesión simultáneos de una misma identidad
    // nueva crean exactamente una cuenta. Se lanzan 10 a la vez para forzar la carrera.
    [Fact]
    public async Task SimultaneousFirstSignIns_CreateExactlyOneAccount()
    {
        var subject = NewSubject();
        var token = FakeGoogleProvider.ValidToken(subject);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => SignInAsync(token)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var ids = new HashSet<Guid>();
        foreach (var response in responses)
            ids.Add((await ReadJsonAsync(response)).GetProperty("player").GetProperty("id").GetGuid());
        Assert.Single(ids);
        Assert.Equal(1, await factory.CountAccountsAsync(subject));
    }

    // RNF-SEG-01: la API solo confía en credenciales emitidas por ella misma.
    // Un token con el emisor de Google (aunque esté bien formado) se rechaza.
    [Fact]
    public async Task TokenNotIssuedByPokeArena_IsRejected()
    {
        var foreignToken = CreateJwt(
            signingKey: AuthApiFactory.SigningKey,
            issuer: "https://accounts.google.com",
            audience: "pokearena-api");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignToken);
        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Un token con emisor y audiencia correctos pero firmado con otra clave también se rechaza.
    [Fact]
    public async Task TokenSignedWithAnotherKey_IsRejected()
    {
        var forged = CreateJwt(
            signingKey: "otra-clave-que-no-es-la-de-pokearena-123",
            issuer: "pokearena",
            audience: "pokearena-api");

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutCredential_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Cada prueba usa un "sub" distinto para no chocar con las demás en la base compartida.
    private static string NewSubject() => "google-" + Guid.NewGuid().ToString("N");

    private async Task<HttpResponseMessage> SignInAsync(string idToken)
    {
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync("/api/auth/google", new { idToken });
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    // Fabrica un JWT HS256 a mano para las pruebas de rechazo.
    private static string CreateJwt(string signingKey, string issuer, string audience) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString(),
                ["role"] = IdentityRoles.Player
            }
        });
}

// Las pruebas de esta colección comparten una sola API y una sola base de datos.
[CollectionDefinition("Auth API")]
public sealed class AuthApiCollection : ICollectionFixture<AuthApiFactory>;

// FACTORÍA DE LA API PARA LAS PRUEBAS DE IDENTIDAD.
public sealed class AuthApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Misma clave que CardsApiFactory: las dos factorías pueden correr al mismo tiempo
    // en el mismo proceso y la variable de entorno es compartida.
    public const string SigningKey = "test-signing-key-that-is-long-enough";

    // Base de datos PROPIA de estas pruebas. CardsApiFactory borra y recrea
    // pokearena_tests al iniciar, y si se compartiera la base, una factoría le borraría
    // los datos a la otra mientras corren en paralelo.
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=pokearena_auth_tests;Username=pokearena;Password=local_dev_password";

    public AuthApiFactory()
    {
        // AddIdentityModule lee la clave mientras Program.cs arranca. En ese momento la
        // configuración en memoria todavía no está aplicada, por eso también va como
        // variable de entorno (el mismo truco que usa CardsApiFactory).
        Environment.SetEnvironmentVariable("Jwt__SigningKey", SigningKey);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Jwt:SigningKey"] = SigningKey
            }));
        builder.ConfigureServices(services =>
        {
            // Se quita el adaptador real de Google y se pone el falso.
            services.RemoveAll<IExternalIdentityProvider>();
            services.AddSingleton<IExternalIdentityProvider, FakeGoogleProvider>();
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        // Base limpia con todas las migraciones, incluida AddPlayerAccounts.
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task<int> CountAccountsAsync(string subject)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        return await db.PlayerAccounts.CountAsync(account => account.ProviderSubject == subject);
    }

    public async Task<int> CountAllAccountsWithEmailAsync(string email)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        return await db.PlayerAccounts.CountAsync(account => account.Email == email);
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}

// DOBLE: "Google" de mentira para las pruebas de integración.
// Interpreta el texto del token en lugar de validar firmas:
// "valid|<sub>|<email>" → identidad válida
// "invalid"            → token rechazado (401)
// "unavailable"        → Google caído (503)
public sealed class FakeGoogleProvider : IExternalIdentityProvider
{
    public const string InvalidToken = "invalid";
    public const string UnavailableToken = "unavailable";
    public const string InvalidMarkerEmail = "nunca-deberia-existir@test.com";

    public static string ValidToken(string subject, string email = "trainer@test.com") => $"valid|{subject}|{email}";

    public Task<ExternalIdentityResult> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        var parts = idToken.Split('|');
        var result = parts[0] switch
        {
            "valid" => ExternalIdentityResult.Valid(new ExternalIdentity("Google", parts[1], parts[2])),
            UnavailableToken => ExternalIdentityResult.Unavailable("Google no responde (simulado)."),
            _ => ExternalIdentityResult.InvalidToken("Token rechazado (simulado).")
        };
        return Task.FromResult(result);
    }
}
