using System.Net;
using System.Text.Json;

namespace PokeArena.Tests;

[Collection("Cards API")]
public sealed class CatalogEndpointIntegrationTests(CardsApiFactory factory)
{
    [Fact]
    public async Task Catalog_RejectsInvalidPowerAndPageSize()
    {
        var client = CreatePlayerClient();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/v1/cards/catalog?minPower=21")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/v1/cards/catalog?page=-1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/v1/cards/catalog?pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task Catalog_ReturnsEmptyListForValidNoMatch()
    {
        var response = await CreatePlayerClient()
            .GetAsync("/api/v1/cards/catalog?search=does-not-exist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Catalog_OnlyOwnedReturnsOnlyCardsOwnedByCurrentSession()
    {
        var playerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        await factory.AddOwnedAvailableCardAsync(playerId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Player");
        client.DefaultRequestHeaders.Add("X-Test-Subject", playerId.ToString());

        var response = await client.GetAsync("/api/v1/cards/catalog?onlyOwned=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, item =>
        {
            Assert.True(item.GetProperty("isOwned").GetBoolean());
            Assert.True(item.GetProperty("quantityOwned").GetInt32() > 0);
        });
    }

    private HttpClient CreatePlayerClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Player");
        return client;
    }
}
