using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PokeArena.Api.Data;
using PokeArena.Api.Models;
using PokeArena.Api.Modules.Identity.Application;

namespace PokeArena.Tests;

// PRUEBAS DE INTEGRACIÓN de la colección inicial.
//
// Aquí corre la API COMPLETA en memoria contra PostgreSQL (base propia de estas pruebas) y
// se hacen solicitudes HTTP reales: el jugador inicia sesión (con un Google falso) y la
// entrega ocurre de verdad dentro del login. Estas pruebas demuestran lo que las unitarias
// no pueden: el candado, las restricciones de la base de datos y la concurrencia.
[Collection("Collection API")]
public class CollectionIntegrationTests(CollectionApiFactory factory)
{
    // CRITERIO DE ÉXITO: primer ingreso con suficientes Comunes disponibles → se entregan
    // 15 distintas y 1.000 cubos. Además demuestra la decisión H: las cartas NO disponibles
    // y las de otra rareza no cuentan.
    [Fact]
    public async Task FirstSignIn_WithEnoughCommons_GrantsFifteenOnlyAvailableCommonsAndTheCubes()
    {
        await factory.ResetCatalogAsync(availableCommons: 20, unavailableCommons: 3, availableRares: 5);

        var (client, body) = await SignInAsync(NewSubject());

        Assert.Equal("Granted", body.GetProperty("initialCollection").GetString());
        var collection = await GetCollectionAsync(client);
        Assert.Equal(1000, collection.GetProperty("cubes").GetInt32());
        var cards = collection.GetProperty("cards").EnumerateArray().ToList();
        Assert.Equal(15, cards.Count);
        Assert.All(cards, card =>
        {
            Assert.Equal(1, card.GetProperty("copies").GetInt32());
            // COM-: Común disponible. NOD-: Común no disponible. RAR-: otra rareza.
            Assert.StartsWith("COM-", card.GetProperty("code").GetString());
        });
    }

    // CRITERIO DE ÉXITO: ingresos posteriores no vuelven a entregar ni a regalar cubos.
    [Fact]
    public async Task SecondSignIn_ReturnsAlreadyGrantedAndNothingChanges()
    {
        await factory.ResetCatalogAsync(availableCommons: 20);
        var subject = NewSubject();
        var (_, first) = await SignInAsync(subject);

        var (client, second) = await SignInAsync(subject);

        Assert.Equal("Granted", first.GetProperty("initialCollection").GetString());
        Assert.Equal("AlreadyGranted", second.GetProperty("initialCollection").GetString());
        var collection = await GetCollectionAsync(client);
        Assert.Equal(1000, collection.GetProperty("cubes").GetInt32());
        Assert.Equal(15, collection.GetProperty("cards").GetArrayLength());
    }

    // CRITERIO DE FALLO: con menos de 15 Comunes disponibles la cuenta SÍ se crea, pero sin
    // cartas ni cubos, y la entrega se completa en un ingreso posterior. Las cartas no
    // disponibles no cuentan: hay 14 disponibles y 5 no disponibles.
    [Fact]
    public async Task FewerThanFifteenAvailableCommons_CreatesTheAccountWithoutDelivery_AndCompletesLater()
    {
        await factory.ResetCatalogAsync(availableCommons: 14, unavailableCommons: 5);
        var subject = NewSubject();

        var (client, first) = await SignInAsync(subject);

        Assert.True(first.GetProperty("isNewAccount").GetBoolean());
        Assert.Equal("NotAvailable", first.GetProperty("initialCollection").GetString());
        var empty = await GetCollectionAsync(client);
        Assert.Equal(0, empty.GetProperty("cubes").GetInt32());
        Assert.Equal(0, empty.GetProperty("cards").GetArrayLength());

        // El equipo agrega una Común disponible más: ahora hay 15.
        await factory.AddAvailableCommonsAsync(1, firstNumber: 900);

        var (laterClient, second) = await SignInAsync(subject);

        Assert.False(second.GetProperty("isNewAccount").GetBoolean());
        Assert.Equal("Granted", second.GetProperty("initialCollection").GetString());
        var full = await GetCollectionAsync(laterClient);
        Assert.Equal(1000, full.GetProperty("cubes").GetInt32());
        Assert.Equal(15, full.GetProperty("cards").GetArrayLength());
    }

    // EXACTAMENTE UNA VEZ: ocho primeros ingresos SIMULTÁNEOS del mismo jugador producen una
    // sola entrega. Es la prueba del candado por jugador y de la clave primaria.
    [Fact]
    public async Task SimultaneousFirstSignIns_OfTheSamePlayer_DeliverExactlyOnce()
    {
        await factory.ResetCatalogAsync(availableCommons: 20);
        var subject = NewSubject();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SignInAsync(subject)));

        var outcomes = responses.Select(result => result.Body.GetProperty("initialCollection").GetString()).ToList();
        Assert.Equal(1, outcomes.Count(outcome => outcome == "Granted"));

        var playerId = responses[0].Body.GetProperty("player").GetProperty("id").GetGuid();
        var counts = await factory.CountRowsAsync(playerId);
        Assert.Equal(1, counts.Deliveries);
        Assert.Equal(15, counts.Copies);
        Assert.Equal(1, counts.Movements);
        Assert.Equal(1000, counts.Balance);
    }

    // RENDIMIENTO DE LA ENTREGA: dos jugadores distintos no se esperan entre sí. Ambos
    // reciben su colección aunque inicien sesión al mismo tiempo.
    [Fact]
    public async Task SimultaneousFirstSignIns_OfDifferentPlayers_AreAllGranted()
    {
        await factory.ResetCatalogAsync(availableCommons: 20);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => SignInAsync(NewSubject())));

        Assert.All(responses, result =>
            Assert.Equal("Granted", result.Body.GetProperty("initialCollection").GetString()));
    }

    // OE-2: el saldo guardado coincide con la suma de sus movimientos. La consulta de
    // verificación debe devolver CERO jugadores con diferencias.
    [Fact]
    public async Task CubeBalances_AlwaysMatchTheirMovements()
    {
        await factory.ResetCatalogAsync(availableCommons: 20);
        await SignInAsync(NewSubject());
        await SignInAsync(NewSubject());

        var withDifferences = await factory.PlayersWithBalanceDifferencesAsync();

        Assert.Empty(withDifferences);
    }

    // La consulta de la colección exige estar autenticado.
    [Fact]
    public async Task GetCollection_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/me/collection");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------- RESTRICCIONES DE LA BASE DE DATOS ----
    // Estas pruebas escriben SQL directo para demostrar que la base se defiende sola, aunque
    // el código de la aplicación se equivocara.

    // El saldo nunca puede ser negativo (RNF-INT-03).
    [Fact]
    public async Task Database_RejectsANegativeBalance()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => factory.ExecuteSqlAsync(
            "INSERT INTO cube_balances (\"PlayerId\", \"Balance\", \"UpdatedAt\") VALUES ({0}, -1, now())",
            Guid.NewGuid()));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    // Un movimiento de cero cubos no tiene sentido y se rechaza.
    [Fact]
    public async Task Database_RejectsAMovementOfZeroCubes()
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => factory.ExecuteSqlAsync(
            "INSERT INTO cube_movements (\"PlayerId\", \"Amount\", \"Reason\", \"CreatedAt\") VALUES ({0}, 0, 'InitialGrant', now())",
            Guid.NewGuid()));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    // La clave primaria de initial_deliveries es la última barrera de "exactamente una vez":
    // la base se niega a guardar una segunda entrega del mismo jugador.
    [Fact]
    public async Task Database_RejectsASecondInitialDeliveryOfTheSamePlayer()
    {
        var playerId = Guid.NewGuid();
        const string sql = "INSERT INTO initial_deliveries (\"PlayerId\", \"DeliveredAt\") VALUES ({0}, now())";
        await factory.ExecuteSqlAsync(sql, playerId);

        var error = await Assert.ThrowsAsync<PostgresException>(() => factory.ExecuteSqlAsync(sql, playerId));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    // -------------------------------------------------------------------- AYUDAS ----

    private static string NewSubject() => $"collection-{Guid.NewGuid():N}";

    // Inicia sesión con un token de Google falso. Devuelve un cliente HTTP que ya lleva la
    // credencial de PokéArena y el cuerpo de la respuesta. Cada llamada usa su PROPIO cliente
    // para poder lanzar varias en paralelo.
    private async Task<(HttpClient Client, JsonElement Body)> SignInAsync(string subject)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/google",
            new { idToken = FakeGoogleProvider.ValidToken(subject) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return (client, body);
    }

    private static async Task<JsonElement> GetCollectionAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me/collection");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

// Agrupa las pruebas de integración de la colección bajo UNA sola factoría, para que no
// corran en paralelo entre sí (comparten la base de datos).
[CollectionDefinition("Collection API")]
public sealed class CollectionApiCollection : ICollectionFixture<CollectionApiFactory>;

// FACTORÍA: levanta la API completa en memoria con una base de datos PROPIA.
//
// Es casi igual a AuthApiFactory, con dos diferencias: usa su propia base
// (pokearena_collection_tests), porque estas pruebas llenan el catálogo de cartas y eso
// alteraría las pruebas de Inicio de sesión; y trae ayudas para preparar el catálogo y
// contar filas. Google se reemplaza por FakeGoogleProvider (definido en SignInEndpointTests.cs).
public sealed class CollectionApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=pokearena_collection_tests;Username=pokearena;Password=local_dev_password";

    public CollectionApiFactory()
    {
        // La clave debe estar en una variable de entorno porque Program.cs la lee mientras
        // arranca, antes de que se aplique la configuración en memoria (mismo truco que
        // AuthApiFactory y CardsApiFactory).
        Environment.SetEnvironmentVariable("Jwt__SigningKey", AuthApiFactory.SigningKey);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Jwt:SigningKey"] = AuthApiFactory.SigningKey
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IExternalIdentityProvider>();
            services.AddSingleton<IExternalIdentityProvider, FakeGoogleProvider>();
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    // Deja el catálogo con exactamente las cartas pedidas. Los códigos dicen de qué tipo
    // es cada una: COM- (Común disponible), NOD- (Común NO disponible), RAR- (Rara disponible).
    public async Task ResetCatalogAsync(int availableCommons, int unavailableCommons = 0, int availableRares = 0)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE cards RESTART IDENTITY;");

        var cards = new List<Card>();
        cards.AddRange(Enumerable.Range(1, availableCommons).Select(n => NewCard($"COM-{n:000}", "Común", true)));
        cards.AddRange(Enumerable.Range(1, unavailableCommons).Select(n => NewCard($"NOD-{n:000}", "Común", false)));
        cards.AddRange(Enumerable.Range(1, availableRares).Select(n => NewCard($"RAR-{n:000}", "Rara", true)));
        db.Cards.AddRange(cards);
        await db.SaveChangesAsync();
    }

    // Agrega Comunes disponibles sin tocar las que ya existen (simula que el equipo carga más).
    public async Task AddAvailableCommonsAsync(int count, int firstNumber)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        db.Cards.AddRange(Enumerable.Range(firstNumber, count).Select(n => NewCard($"COM-{n:000}", "Común", true)));
        await db.SaveChangesAsync();
    }

    // Cuenta las filas que una entrega deja para un jugador.
    public async Task<(int Deliveries, int Copies, int Movements, int Balance)> CountRowsAsync(Guid playerId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        return (
            await db.InitialDeliveries.CountAsync(delivery => delivery.PlayerId == playerId),
            await db.CardCopies.CountAsync(copy => copy.PlayerId == playerId),
            await db.CubeMovements.CountAsync(movement => movement.PlayerId == playerId),
            await db.CubeBalances.Where(balance => balance.PlayerId == playerId).Select(balance => balance.Balance).SingleOrDefaultAsync());
    }

    // VERIFICACIÓN DE OE-2: jugadores cuyo saldo guardado difiere de la suma de sus
    // movimientos. Debe devolver vacío SIEMPRE. Es la misma consulta que usaría Operaciones.
    public async Task<List<Guid>> PlayersWithBalanceDifferencesAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        return await db.Database.SqlQueryRaw<Guid>(
            "SELECT b.\"PlayerId\" AS \"Value\" FROM cube_balances b " +
            "WHERE b.\"Balance\" <> (SELECT COALESCE(SUM(m.\"Amount\"), 0) FROM cube_movements m " +
            "WHERE m.\"PlayerId\" = b.\"PlayerId\")").ToListAsync();
    }

    // Ejecuta SQL directo (para las pruebas de restricciones de la base de datos).
    public async Task ExecuteSqlAsync(string sql, params object[] parameters)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    private static Card NewCard(string code, string rarity, bool available) => new()
    {
        Code = code,
        Name = $"Carta {code}",
        Type = "Fuego",
        Rarity = rarity,
        Cost = 1,
        Power = 1,
        IsAvailable = available,
        ImageStatus = available ? "Lista" : "Fallida",
        ImageUrl = $"/uploads/cards/{code}-large.png",
        ThumbnailUrl = available ? $"/uploads/cards/{code}-small.webp" : null,
    };

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}
