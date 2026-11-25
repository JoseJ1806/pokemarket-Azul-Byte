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
using PokeArena.Api.Modules.Locations.Domain;

namespace PokeArena.Tests;

// PRUEBAS DE INTEGRACIÓN del emparejamiento (US #127 Buscar partida y #128 Consultar y salir
// de la búsqueda).
//
// Corre la API COMPLETA en memoria contra PostgreSQL, con su propia base. Cada jugador inicia
// sesión con un Google falso, recibe su colección inicial y arma un deck de 12 cartas; después
// busca partida por HTTP, igual que la pantalla. Así se prueban de verdad el candado por
// jugador, el índice único de búsquedas abiertas, el bloqueo de filas al emparejar y la creación
// de la partida en el módulo Partida.
//
// El reloj de la API es MatchmakingTestClock: la hora real más un desfase que las pruebas
// adelantan para simular el paso del tiempo (rango de Elo, inactividad, oferta del bot).
[Collection("Matchmaking API")]
public class MatchmakingIntegrationTests(MatchmakingApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- BUSCAR (#127) ----

    // ERROR: buscar partida sin indicar un deck.
    [Fact]
    public async Task Search_WithoutADeck_IsRejected()
    {
        var player = await NewPlayerAsync();

        var response = await SearchAsync(player, deckId: null);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "deck-required");
    }

    // ERROR: un deck que pertenece a otro jugador se rechaza como si no existiera.
    [Fact]
    public async Task Search_WithTheDeckOfAnotherPlayer_IsRejected()
    {
        var player = await NewPlayerAsync();
        var other = await NewPlayerAsync();

        var response = await SearchAsync(player, other.DeckId);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "deck-not-found");
    }

    // ERROR: con menos de 8 cubos no se puede buscar, y el mensaje dice el saldo.
    [Fact]
    public async Task Search_WithLessThanEightCubes_IsRejectedShowingTheBalance()
    {
        var player = await NewPlayerAsync();
        await factory.SetCubesAsync(player.Id, 5);

        var response = await SearchAsync(player, player.DeckId);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "insufficient-cubes");
        Assert.Contains("Tienes 5 cubos", problem.GetProperty("detail").GetString());
        Assert.Equal(0, await factory.CountSearchesAsync(player.Id));
    }

    // ÉXITO: el primero queda en espera; el segundo se empareja con él y se crea EXACTAMENTE
    // una partida con los dos jugadores y el deck que indicó cada uno.
    [Fact]
    public async Task TwoPlayers_AreMatched_IntoExactlyOneMatchWithTheirOwnDecks()
    {
        var first = await NewPlayerAsync();
        var second = await NewPlayerAsync();

        var firstResponse = await SearchAsync(first, first.DeckId);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal("Waiting", (await ReadAsync(firstResponse)).GetProperty("status").GetString());

        var secondResponse = await SearchAsync(second, second.DeckId);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var secondSearch = await ReadAsync(secondResponse);
        Assert.Equal("MatchFound", secondSearch.GetProperty("status").GetString());
        var matchId = secondSearch.GetProperty("matchId").GetGuid();

        var firstSearch = await GetSearchAsync(first);
        Assert.Equal("MatchFound", firstSearch.GetProperty("status").GetString());
        Assert.Equal(matchId, firstSearch.GetProperty("matchId").GetGuid());

        var match = Assert.Single(await factory.GetMatchesAsync());
        Assert.Equal(matchId, match.Id);
        Assert.Equal(first.Id, match.ChallengerId);
        Assert.Equal(second.Id, match.OpponentId);
        Assert.Equal(first.DeckCardIds.Order().ToList(), match.ChallengerCardIds.Order().ToList());
        Assert.Equal(second.DeckCardIds.Order().ToList(), match.OpponentCardIds.Order().ToList());
    }

    // ALTERNO: doble clic, dos pestañas o un reintento dejan UNA sola búsqueda abierta.
    [Fact]
    public async Task SearchingManyTimesAtOnce_WithTheSameDeck_LeavesASingleOpenSearch()
    {
        var player = await NewPlayerAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SearchAsync(player, player.DeckId)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, response =>
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        var searchIds = new HashSet<Guid>();
        foreach (var response in responses)
            searchIds.Add((await ReadAsync(response)).GetProperty("searchId").GetGuid());
        Assert.Single(searchIds);
        Assert.Equal(1, await factory.CountOpenSearchesAsync(player.Id));
    }

    // ALTERNO: el deck no se puede cambiar con la búsqueda abierta.
    [Fact]
    public async Task SearchingWithAnotherDeck_WhileTheSearchIsOpen_IsRejected()
    {
        var player = await NewPlayerAsync();
        var otherDeck = await CreateDeckAsync(player.Client, "Segundo", player.CollectionCardIds.Skip(3).Take(12).ToList());
        Assert.Equal(HttpStatusCode.Created, (await SearchAsync(player, player.DeckId)).StatusCode);

        var response = await SearchAsync(player, otherDeck.Id);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "search-already-open");
        Assert.Equal(1, await factory.CountOpenSearchesAsync(player.Id));
    }

    // ERROR: una sola búsqueda abierta sin importar el contexto. Una búsqueda de torneo abierta
    // (se inserta directo, porque los torneos todavía no existen) bloquea la clasificatoria.
    [Fact]
    public async Task AnOpenSearchInAnotherContext_BlocksTheRankedSearch()
    {
        var player = await NewPlayerAsync();
        var tournamentSearchId = await factory.InsertOpenSearchAsync(player.Id, "Tournament");

        var response = await SearchAsync(player, player.DeckId);

        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "search-already-open");
        var detail = problem.GetProperty("detail").GetString();
        Assert.Contains("Tournament", detail);
        Assert.Contains(tournamentSearchId.ToString(), detail);
    }

    // ERROR: un jugador con una partida activa no puede buscar otra.
    [Fact]
    public async Task APlayerWithAnActiveMatch_CannotSearchAgain()
    {
        var (first, _, matchId) = await MatchTwoPlayersAsync();

        var response = await SearchAsync(first, first.DeckId);

        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "active-match");
        Assert.Contains(matchId.ToString(), problem.GetProperty("detail").GetString());
        Assert.Single(await factory.GetMatchesAsync());
    }

    // ÉXITO: Elo cercano. Con 20 puntos de diferencia no se emparejan al inicio (rango 6);
    // a los 25 segundos el rango llega a 21 y se emparejan.
    [Fact]
    public async Task TheEloRange_WidensWithTheWaitingTime()
    {
        var low = await NewPlayerAsync();
        var high = await NewPlayerAsync();
        await factory.SetEloAsync(high.Id, 20);

        Assert.Equal(HttpStatusCode.Created, (await SearchAsync(low, low.DeckId)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SearchAsync(high, high.DeckId)).StatusCode);

        // Cada 5 segundos los dos consultan, como haría la pantalla.
        for (var second = 5; second <= 20; second += 5)
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("Waiting", (await GetSearchAsync(low)).GetProperty("status").GetString());
            Assert.Equal("Waiting", (await GetSearchAsync(high)).GetProperty("status").GetString());
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(5));
        var lowSearch = await GetSearchAsync(low);
        Assert.Equal("MatchFound", lowSearch.GetProperty("status").GetString());
        Assert.True(lowSearch.GetProperty("eloRange").GetInt32() >= 20);
        Assert.Equal("MatchFound", (await GetSearchAsync(high)).GetProperty("status").GetString());
        Assert.Single(await factory.GetMatchesAsync());
    }

    // ALTERNO: si la partida no se puede crear, nadie queda bloqueado: los dos vuelven a la
    // búsqueda con un mensaje. Cuando el problema se resuelve, se emparejan normalmente.
    [Fact]
    public async Task WhenTheMatchCannotBeCreated_BothPlayersReturnToTheSearch()
    {
        var first = await NewPlayerAsync();
        var second = await NewPlayerAsync();
        await factory.SetLocationsAvailableAsync(false);

        await SearchAsync(first, first.DeckId);
        var secondSearch = await ReadAsync(await SearchAsync(second, second.DeckId));

        Assert.Equal("Waiting", secondSearch.GetProperty("status").GetString());
        Assert.Contains("ubicaciones", secondSearch.GetProperty("message").GetString());
        Assert.Equal("Waiting", (await GetSearchAsync(first)).GetProperty("status").GetString());
        Assert.Empty(await factory.GetMatchesAsync());

        await factory.SetLocationsAvailableAsync(true);
        var firstSearch = await GetSearchAsync(first);
        Assert.Equal("MatchFound", firstSearch.GetProperty("status").GetString());
        Assert.Equal(
            firstSearch.GetProperty("matchId").GetGuid(),
            (await GetSearchAsync(second)).GetProperty("matchId").GetGuid());
        Assert.Single(await factory.GetMatchesAsync());
    }

    // ALTERNO: aunque muchos jugadores busquen y consulten AL MISMO TIEMPO (como varias
    // instancias de la API), nadie queda en dos partidas.
    [Fact]
    public async Task ManyPlayersSearchingAtTheSameTime_NeverEndUpInTwoMatches()
    {
        var players = new List<TestPlayer>();
        for (var i = 0; i < 12; i++)
            players.Add(await NewPlayerAsync());

        await Task.WhenAll(players.Select(player => SearchAsync(player, player.DeckId)));

        // Rondas de consultas en paralelo hasta que todos tengan partida.
        var statuses = new Dictionary<Guid, JsonElement>();
        for (var round = 0; round < 15; round++)
        {
            var results = await Task.WhenAll(players.Select(async player => (player.Id, Search: await GetSearchAsync(player))));
            foreach (var (playerId, search) in results)
                statuses[playerId] = search;
            if (statuses.Values.All(search => search.GetProperty("status").GetString() == "MatchFound"))
                break;
        }

        Assert.All(statuses.Values, search => Assert.Equal("MatchFound", search.GetProperty("status").GetString()));

        var matches = await factory.GetMatchesAsync();
        Assert.Equal(6, matches.Count);
        foreach (var player in players)
        {
            var match = Assert.Single(matches, item => item.ChallengerId == player.Id || item.OpponentId == player.Id);
            Assert.Equal(match.Id, statuses[player.Id].GetProperty("matchId").GetGuid());
        }
    }

    // --------------------------------------------------- CONSULTAR Y SALIR (#128) ----

    // ÉXITO: salir deja la búsqueda cancelada, y una búsqueda cancelada nunca se empareja.
    [Fact]
    public async Task LeavingTheSearch_CancelsIt_AndItIsNeverMatched()
    {
        var leaver = await NewPlayerAsync();
        var other = await NewPlayerAsync();
        await SearchAsync(leaver, leaver.DeckId);

        var leave = await leaver.Client.DeleteAsync("/api/me/match-search");
        Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        Assert.Equal("Cancelled", (await ReadAsync(leave)).GetProperty("status").GetString());

        var otherSearch = await ReadAsync(await SearchAsync(other, other.DeckId));
        Assert.Equal("Waiting", otherSearch.GetProperty("status").GetString());
        Assert.Equal("Cancelled", (await GetSearchAsync(leaver)).GetProperty("status").GetString());
        Assert.Empty(await factory.GetMatchesAsync());
    }

    // ERROR: salir de una búsqueda que ya encontró partida se rechaza e indica la partida.
    [Fact]
    public async Task LeavingASearchThatAlreadyFoundAMatch_IsRejectedShowingTheMatch()
    {
        var (first, _, matchId) = await MatchTwoPlayersAsync();

        var response = await first.Client.DeleteAsync("/api/me/match-search");

        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "match-already-found");
        Assert.Contains(matchId.ToString(), problem.GetProperty("detail").GetString());
    }

    // ALTERNO: salir y emparejar al mismo tiempo da un resultado consistente: o la búsqueda
    // queda cancelada sin partida, o existe la partida y la salida se rechaza.
    [Fact]
    public async Task LeavingAndBeingMatchedAtTheSameTime_IsConsistent()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await factory.ResetAsync();
            var leaver = await NewPlayerAsync();
            var rival = await NewPlayerAsync();
            await SearchAsync(leaver, leaver.DeckId);

            var leaveTask = leaver.Client.DeleteAsync("/api/me/match-search");
            var searchTask = SearchAsync(rival, rival.DeckId);
            var leave = await leaveTask;
            await searchTask;

            var matches = await factory.GetMatchesAsync();
            var leaverMatches = matches.Where(match => match.ChallengerId == leaver.Id || match.OpponentId == leaver.Id).ToList();
            var leaverSearch = await GetSearchAsync(leaver);

            if (leave.StatusCode == HttpStatusCode.OK)
            {
                Assert.Empty(leaverMatches);
                Assert.Equal("Cancelled", leaverSearch.GetProperty("status").GetString());
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
                var match = Assert.Single(leaverMatches);
                Assert.Equal("MatchFound", leaverSearch.GetProperty("status").GetString());
                Assert.Equal(match.Id, leaverSearch.GetProperty("matchId").GetGuid());
            }
        }
    }

    // ERROR: un jugador que deja de consultar más de 20 segundos queda fuera y nunca se empareja.
    [Fact]
    public async Task APlayerWhoStopsChecking_IsNeverMatched_AndTheSearchExpires()
    {
        var absent = await NewPlayerAsync();
        var newcomer = await NewPlayerAsync();
        await SearchAsync(absent, absent.DeckId);

        factory.Clock.Advance(TimeSpan.FromSeconds(21));
        var newcomerSearch = await ReadAsync(await SearchAsync(newcomer, newcomer.DeckId));

        Assert.Equal("Waiting", newcomerSearch.GetProperty("status").GetString());
        Assert.Equal("Expired", (await GetSearchAsync(absent)).GetProperty("status").GetString());
        Assert.Empty(await factory.GetMatchesAsync());
    }

    // ÉXITO: a los 60 segundos sin rival se ofrece jugar contra el bot, y antes no.
    [Fact]
    public async Task TheBotOffer_AppearsAfterSixtySecondsWithoutARival()
    {
        var player = await NewPlayerAsync();
        var created = await ReadAsync(await SearchAsync(player, player.DeckId));
        Assert.False(created.GetProperty("botOfferAvailable").GetBoolean());

        for (var second = 10; second <= 50; second += 10)
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(10));
            Assert.False((await GetSearchAsync(player)).GetProperty("botOfferAvailable").GetBoolean());
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(10));
        var search = await GetSearchAsync(player);
        Assert.Equal("Waiting", search.GetProperty("status").GetString());
        Assert.True(search.GetProperty("botOfferAvailable").GetBoolean());
    }

    // Consultar sin haber buscado nunca.
    [Fact]
    public async Task CheckingWithoutAnySearch_ReturnsNotFound()
    {
        var player = await NewPlayerAsync();

        var response = await player.Client.GetAsync("/api/me/match-search");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "search-not-found");
    }

    // ------------------------------------------------------------ BASE DE DATOS ----

    // El índice único parcial es la última barrera: la base se niega a guardar una segunda
    // búsqueda abierta del mismo jugador, aunque sea de otro contexto.
    [Fact]
    public async Task Database_RejectsASecondOpenSearchOfTheSamePlayer()
    {
        var playerId = Guid.NewGuid();
        await factory.InsertOpenSearchAsync(playerId, "Ranked");

        var error = await Assert.ThrowsAsync<PostgresException>(() => factory.InsertOpenSearchAsync(playerId, "Tournament"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    // -------------------------------------------------------------------- AYUDAS ----

    public sealed record TestPlayer(
        Guid Id,
        HttpClient Client,
        Guid DeckId,
        IReadOnlyList<int> DeckCardIds,
        IReadOnlyList<int> CollectionCardIds);

    // Un jugador nuevo: inicia sesión (recibe 15 cartas y 1.000 cubos) y arma un deck con 12.
    private async Task<TestPlayer> NewPlayerAsync()
    {
        var client = factory.CreateClient();
        var signIn = await client.PostAsJsonAsync(
            "/api/auth/google",
            new { idToken = FakeGoogleProvider.ValidToken($"matchmaking-{Guid.NewGuid():N}") });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        var body = await signIn.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        var playerId = body.GetProperty("player").GetProperty("id").GetGuid();

        var collection = await client.GetFromJsonAsync<JsonElement>("/api/me/collection");
        var cardIds = collection.GetProperty("cards").EnumerateArray()
            .Select(card => card.GetProperty("cardId").GetInt32())
            .ToList();
        Assert.Equal(15, cardIds.Count);

        var deck = await CreateDeckAsync(client, "Principal", cardIds.Take(12).ToList());
        return new TestPlayer(playerId, client, deck.Id, deck.CardIds, cardIds);
    }

    private static async Task<(Guid Id, List<int> CardIds)> CreateDeckAsync(HttpClient client, string name, List<int> cardIds)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/decks")
        {
            Content = JsonContent.Create(new { name, cardIds })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("id").GetGuid(),
            body.GetProperty("cardIds").EnumerateArray().Select(card => card.GetInt32()).ToList());
    }

    private async Task<(TestPlayer First, TestPlayer Second, Guid MatchId)> MatchTwoPlayersAsync()
    {
        var first = await NewPlayerAsync();
        var second = await NewPlayerAsync();
        await SearchAsync(first, first.DeckId);
        var search = await ReadAsync(await SearchAsync(second, second.DeckId));
        Assert.Equal("MatchFound", search.GetProperty("status").GetString());
        return (first, second, search.GetProperty("matchId").GetGuid());
    }

    private static Task<HttpResponseMessage> SearchAsync(TestPlayer player, Guid? deckId) =>
        player.Client.PostAsJsonAsync("/api/me/match-search", new { deckId });

    private static async Task<JsonElement> GetSearchAsync(TestPlayer player)
    {
        var response = await player.Client.GetAsync("/api/me/match-search");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await ReadAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }
}

// Agrupa las pruebas del emparejamiento bajo UNA sola factoría (comparten la base de datos).
[CollectionDefinition("Matchmaking API")]
public sealed class MatchmakingApiCollection : ICollectionFixture<MatchmakingApiFactory>;

// RELOJ DE PRUEBA: la hora real más un desfase que las pruebas adelantan. Se parte de la hora
// real para que los tokens de sesión sigan siendo válidos.
public sealed class MatchmakingTestClock : TimeProvider
{
    private long offsetTicks;

    public override DateTimeOffset GetUtcNow() =>
        DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref offsetTicks));

    public void Advance(TimeSpan time) => Interlocked.Add(ref offsetTicks, time.Ticks);

    public void Reset() => Interlocked.Exchange(ref offsetTicks, 0);
}

// Una partida tal como quedó guardada en la tabla matches.
public sealed record StoredMatch(
    Guid Id,
    Guid ChallengerId,
    Guid OpponentId,
    IReadOnlyList<int> ChallengerCardIds,
    IReadOnlyList<int> OpponentCardIds);

// FACTORÍA: la API completa en memoria con su propia base (pokearena_matchmaking_tests), el
// Google falso y el reloj de prueba. Prepara 20 cartas Comunes (para la colección inicial) y
// una ubicación disponible (para crear partidas).
public sealed class MatchmakingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=pokearena_matchmaking_tests;Username=pokearena;Password=local_dev_password";

    public MatchmakingTestClock Clock { get; } = new();

    public MatchmakingApiFactory()
    {
        // Program.cs lee la clave mientras arranca (mismo truco que CollectionApiFactory).
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
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        db.Cards.AddRange(Enumerable.Range(1, 20).Select(number => new Card
        {
            Code = $"EMP-{number:000}",
            Name = $"Carta de emparejamiento {number}",
            Type = "Agua",
            Rarity = "Común",
            Cost = 1,
            Power = 1,
            IsAvailable = true,
            ImageStatus = "Lista",
            ImageUrl = $"/uploads/cards/EMP-{number:000}-large.png",
            ThumbnailUrl = $"/uploads/cards/EMP-{number:000}-small.webp",
        }));
        db.Locations.Add(new Location
        {
            Name = "Arena de pruebas",
            Description = "Ubicación para las pruebas del emparejamiento.",
            ImageUrl = "/uploads/locations/arena-large.png",
            ThumbnailUrl = "/uploads/locations/arena-small.webp",
            ImageStatus = LocationImageStatus.Ready,
            IsAvailable = true,
            IdempotencyKey = "emparejamiento-ubicacion-1",
            CardLimit = 4,
            EnergyMode = LocationRules.EnergyModeFixed,
            EnergyAmount = 0,
        });
        await db.SaveChangesAsync();
    }

    // Cada prueba empieza con la fila y las partidas vacías, el reloj en la hora real y la
    // ubicación disponible. Los jugadores de pruebas anteriores no estorban: nadie los consulta.
    public async Task ResetAsync()
    {
        Clock.Reset();
        await ExecuteSqlAsync("TRUNCATE TABLE match_searches, matches;");
        await SetLocationsAvailableAsync(true);
    }

    public Task SetCubesAsync(Guid playerId, int balance) =>
        ExecuteSqlAsync("UPDATE cube_balances SET \"Balance\" = {0} WHERE \"PlayerId\" = {1}", balance, playerId);

    public Task SetEloAsync(Guid playerId, int elo) =>
        ExecuteSqlAsync("UPDATE player_accounts SET \"Elo\" = {0} WHERE \"Id\" = {1}", elo, playerId);

    public Task SetLocationsAvailableAsync(bool available) =>
        ExecuteSqlAsync("UPDATE locations SET \"IsAvailable\" = {0}", available);

    // Inserta una búsqueda abierta directo en la tabla (para probar otro contexto y el índice).
    public async Task<Guid> InsertOpenSearchAsync(Guid playerId, string context)
    {
        var id = Guid.NewGuid();
        await ExecuteSqlAsync(
            "INSERT INTO match_searches (\"Id\", \"PlayerId\", \"Context\", \"DeckId\", \"DeckCardIds\", \"Elo\", " +
            "\"Status\", \"CreatedAt\", \"LastSeenAt\") " +
            "VALUES ({0}, {1}, {2}, {3}, '[]'::jsonb, 0, 'Waiting', now(), now() + interval '1 hour')",
            id, playerId, context, Guid.NewGuid());
        return id;
    }

    public async Task<int> CountSearchesAsync(Guid playerId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        return await db.MatchSearches.CountAsync(search => search.PlayerId == playerId);
    }

    public async Task<int> CountOpenSearchesAsync(Guid playerId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        return await db.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::integer AS \"Value\" FROM match_searches " +
                "WHERE \"PlayerId\" = {0} AND \"Status\" IN ('Waiting', 'Pairing')",
                playerId)
            .SingleAsync();
    }

    public async Task<List<StoredMatch>> GetMatchesAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        var rows = await db.Matches.AsNoTracking().ToListAsync();
        return rows
            .Select(match => new StoredMatch(
                match.Id,
                match.ChallengerPlayerId,
                match.OpponentPlayerId,
                JsonSerializer.Deserialize<List<int>>(match.ChallengerDeckCardIds) ?? [],
                JsonSerializer.Deserialize<List<int>>(match.OpponentDeckCardIds) ?? []))
            .ToList();
    }

    public async Task ExecuteSqlAsync(string sql, params object[] parameters)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}
