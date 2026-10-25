using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SkiaSharp;

namespace PokeArena.Tests;

// Pruebas de GET /api/cards/{id}: la ficha de la carta con el detalle de su habilidad.
[Collection("Cards API")]
public class CardDetailTests
{
    private readonly CardsApiFactory factory;

    public CardDetailTests(CardsApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetCard_WithoutCredentials_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/cards/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCard_WithoutAdminRole_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Player");

        var response = await client.GetAsync("/api/cards/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCard_UnknownId_ReturnsNotFoundProblem()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

        var response = await client.GetAsync("/api/cards/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("card-not-found", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCard_ReturnsCardWithAbilityDetail()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "detail-card-001");

        using var content = CreateMultipartContent();
        content.Add(new StringContent("Carta con ficha"), "Name");
        content.Add(new StringContent("4"), "Cost");
        content.Add(new StringContent("10"), "Power");
        content.Add(new StringContent("Planta"), "Type");
        content.Add(new StringContent("Rara"), "Rarity");
        content.Add(new StringContent("HAB-000"), "AbilityCode");
        var created = await client.PostAsync("/api/cards", content);
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
        using var createdJson = JsonDocument.Parse(createdBody);
        var id = createdJson.RootElement.GetProperty("id").GetInt32();

        var response = await client.GetAsync($"/api/cards/{id}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(id, root.GetProperty("id").GetInt32());
        Assert.StartsWith("PKA-PLA-", root.GetProperty("code").GetString());
        Assert.Equal("Planta", root.GetProperty("type").GetString());
        Assert.Equal("Lista", root.GetProperty("imageStatus").GetString());
        Assert.True(root.GetProperty("isAvailable").GetBoolean());

        var ability = root.GetProperty("ability");
        Assert.Equal("HAB-000", ability.GetProperty("code").GetString());
        Assert.True(ability.TryGetProperty("affectedType", out _));
        Assert.False(root.TryGetProperty("idempotencyKey", out _));
    }

    private static MultipartFormDataContent CreateMultipartContent()
    {
        var content = new MultipartFormDataContent();
        var bitmap = new SKBitmap(600, 840);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var file = new ByteArrayContent(data.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "Image", "card.png");
        bitmap.Dispose();
        return content;
    }
}
