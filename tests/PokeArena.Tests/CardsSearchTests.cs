
using System.Net;
using System.Text.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PokeArena.Api.Data;
using PokeArena.Api.Models;
using Xunit;

namespace PokeArena.Tests;

// PRUEBAS DE INTEGRACIÓN DE LA BÚSQUEDA.
// WebApplicationFactory conecta este proyecto con Program.cs sin levantar un
// servidor manual. La factoría crea datos controlados en PostgreSQL de pruebas.
[Collection("Cards API")]
public class CardsSearchTests
{
    private readonly CardsApiFactory factory;

    public CardsSearchTests(CardsApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SearchCards_RequiresAuthenticationAndAdminRole()
    {
        // Comprueba la diferencia entre no autenticarse y no tener rol Admin.
        // CreateClient usa el servidor de prueba construido desde Program.cs.
        var anonymous = factory.CreateClient();
        // Sin identidad, RequireAuthorization debe responder 401 Unauthorized.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/cards")).StatusCode);

        // El handler de prueba crea una identidad válida, pero con rol insuficiente.
        var user = factory.CreateClient();
        user.DefaultRequestHeaders.Add("X-Test-Role", "User");
        // La autenticación pasó, pero la política AdminOnly debe responder 403.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.GetAsync("/api/cards")).StatusCode);
    }

    [Fact]
    public async Task SearchCards_AppliesTextNumericAndCombinedFilters()
    {
        // Verifica búsqueda parcial, tildes, rangos y combinación AND.
        var client = CreateAdminClient();

        // DRAGON debe encontrar Dragón Azul aunque cambien mayúsculas y tilde.
        var accented = await Get(client, "q=DRAGON");
        Assert.Equal(1, accented.GetProperty("total").GetInt32());

        // Todos los filtros se envían juntos y deben afectar la misma consulta.
        var combined = await Get(client,
            "q=drag&minCost=2&maxCost=4&power=120&type=Agua&rarity=Rara&abilityCode=AB-01&abilityTrigger=combate&abilityEffect=roba");
        Assert.Equal(1, combined.GetProperty("total").GetInt32());

        // Una combinación válida sin coincidencias es un 200 con lista vacía.
        var empty = await Get(client, "q=no-existe");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task SearchCards_ValidatesFiltersAndEscapesLikeCharacters()
    {
        var client = CreateAdminClient();
        // Estos valores se rechazan antes de consultar PostgreSQL.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/cards?cost=11")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/cards?power=15")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/cards?minCost=5&maxCost=2")).StatusCode);

        // Los caracteres LIKE se buscan literalmente, no como comodines.
        var literal = await Get(client, "q=%25_%5C");
        Assert.Equal(0, literal.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task SearchCards_UsesCursorSnapshotAndPageLimit()
    {
        // Una carta creada después del primer request queda fuera del snapshot.
        var client = CreateAdminClient();
        // pageSize=2 limita la primera respuesta a dos cartas.
        var first = await Get(client, "pageSize=2");
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal(2, first.GetProperty("pageSize").GetInt32());

        // nextCursor indica cómo pedir la página siguiente sin usar offsets.
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        // La nueva carta se inserta después de iniciar el recorrido.
        await factory.AddCardAsync("Nueva carta", 1);
        // El cursor conserva el snapshot y no incluye esa carta nueva.
        var second = await Get(client, $"pageSize=2&cursor={cursor}");
        var codes = first.GetProperty("items").EnumerateArray()
            .Concat(second.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("code").GetString())
            .ToList();
        // Las páginas combinadas no deben repetir códigos.
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.DoesNotContain("NEW-01", codes);
    }

    [Fact]
    public async Task SearchCards_ReturnsAllRequiredFieldsAndNoAbilityFilter()
    {
        // Confirma el contrato JSON que consume el cliente de cartas.
        var client = CreateAdminClient();
        // El texto especial se normaliza y busca cartas sin código de habilidad.
        var json = await Get(client, "abilityCode=S%C3%8DN%20HABILIDAD");
        var item = json.GetProperty("items").EnumerateArray().First();

        foreach (var property in new[] { "code", "name", "imageUrl", "thumbnailUrl", "imageStatus", "isAvailable", "cost", "power", "type", "rarity", "abilityCode", "abilityTrigger", "abilityEffect", "abilityAffectedType" })
            Assert.True(item.TryGetProperty(property, out _), property);
    }

    private HttpClient CreateAdminClient()
    {
        // Agrega el rol Admin que el endpoint exige mediante su política.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        return client;
    }

    private static async Task<JsonElement> Get(HttpClient client, string query)
    {
        // Envía la solicitud HTTP y convierte el JSON a un árbol inspeccionable.
        var response = await client.GetAsync($"/api/cards?{query}");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}

public sealed class CardsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly SemaphoreSlim DatabaseLock = new(1, 1);

    // Esta base está separada de la base de desarrollo para que los tests puedan
    // limpiar y volver a sembrar cartas sin destruir datos reales.
    private readonly string connectionString =
        "Host=localhost;Port=5432;Database=pokearena_tests;Username=pokearena;Password=local_dev_password";

    public CardsApiFactory()
    {
        // La clave solo existe dentro del proceso de pruebas; no es una credencial real.
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-signing-key-that-is-long-enough");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // El host usa Development para activar la configuración local de la API.
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:SigningKey"] = "test-signing-key-that-is-long-enough"
            }));
        builder.ConfigureServices(services =>
        {
            // Reemplaza autenticación JWT por un handler controlado por los tests.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        });
    }

    public async Task InitializeAsync()
    {
        // La base de pruebas es PostgreSQL separada y se llena con datos controlados.
        await DatabaseLock.WaitAsync();
        try
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
            // Solo se recrea la base aislada de tests para aplicar todas las migraciones.
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE cards RESTART IDENTITY;");
            // Los datos no dependen de cartas existentes en la base del desarrollador.
            db.Cards.AddRange(
                new Card { Code = "DRG-01", Name = "Dragón Azul", ImageUrl = "https://img/1", ImageStatus = "Lista", Cost = 3, Power = 120, Type = "Agua", Rarity = "Rara", AbilityCode = "AB-01", AbilityTrigger = "Combate", AbilityEffect = "Roba una carta" },
                new Card { Code = "PIK-01", Name = "Pikachu", ImageUrl = "https://img/2", ImageStatus = "Lista", Cost = 1, Power = 80, Type = "Electrico", Rarity = "Comun" },
                new Card { Code = "PIK-02", Name = "Pikachu V", ImageUrl = "https://img/3", ImageStatus = "Pendiente", Cost = 2, Power = 100, Type = "Electrico", Rarity = "Rara" },
                new Card { Code = "FIR-01", Name = "Fuego", ImageUrl = "https://img/4", ImageStatus = "Lista", Cost = 5, Power = 200, Type = "Fuego", Rarity = "Comun" });
            await db.SaveChangesAsync();
        }
        finally
        {
            DatabaseLock.Release();
        }
    }

    public async Task AddCardAsync(string name, int cost)
    {
        // Simula una carta creada mientras otro administrador recorre resultados.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        db.Cards.Add(new Card { Code = "NEW-01", Name = name, ImageStatus = "Pendiente", Cost = cost, Type = "Agua", Rarity = "Comun" });
        await db.SaveChangesAsync();
    }

    public async Task<int> CountCardsByIdempotencyKeyAsync(string key)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        return await db.Cards.CountAsync(card => card.IdempotencyKey == key);
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    // Sustituye JWT únicamente dentro de las pruebas para demostrar 401, 403 y Admin.
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Sin X-Test-Role se simula una petición anónima.
        if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
            return Task.FromResult(AuthenticateResult.NoResult());
        // Con X-Test-Role se crea una identidad para ejercitar la política real.
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role.ToString()) }, "Test");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
    }
}
