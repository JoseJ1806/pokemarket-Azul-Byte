using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SkiaSharp;

namespace PokeArena.Tests;

// Pruebas de integración de POST /api/locations con el mismo host y PostgreSQL aislado
// que las pruebas de cartas.
[Collection("Cards API")]
public class LocationCreationTests
{
    private readonly CardsApiFactory factory;

    public LocationCreationTests(CardsApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task CreateLocation_WithoutCredentials_ReturnsUnauthorized()
    {
        using var content = BuildContent();

        var response = await factory.CreateClient().PostAsync("/api/locations", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateLocation_WithoutAdminRole_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "User");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "loc-forbidden-001");
        using var content = BuildContent();

        var response = await client.PostAsync("/api/locations", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateLocation_ValidRequest_ReturnsCreatedWithBothImages()
    {
        using var client = AdminClient("loc-create-001");
        using var content = BuildContent(new()
        {
            ["EffectType"] = "RestriccionPorTurno",
            ["EffectParameters"] = "{\"turnos\":[2,1]}"
        });

        var response = await client.PostAsync("/api/locations", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Lista", json.RootElement.GetProperty("imageStatus").GetString());
        Assert.True(json.RootElement.GetProperty("isAvailable").GetBoolean());
        Assert.Contains("-small.webp", json.RootElement.GetProperty("thumbnailUrl").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("effectParameters").GetProperty("turnos")[0].GetInt32());
    }

    [Fact]
    public async Task CreateLocation_RetryWithSameKey_ReturnsOkWithSameId()
    {
        using var client = AdminClient("loc-retry-001");

        using var firstContent = BuildContent();
        var first = await client.PostAsync("/api/locations", firstContent);
        using var secondContent = BuildContent();
        var second = await client.PostAsync("/api/locations", secondContent);

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.True(first.StatusCode == HttpStatusCode.Created, firstBody);
        Assert.True(second.StatusCode == HttpStatusCode.OK, secondBody);
        using var firstJson = JsonDocument.Parse(firstBody);
        using var secondJson = JsonDocument.Parse(secondBody);
        Assert.Equal(
            firstJson.RootElement.GetProperty("id").GetInt32(),
            secondJson.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task CreateLocation_SameKeyWithDifferentData_ReturnsConflict()
    {
        using var client = AdminClient("loc-conflict-001");

        using var firstContent = BuildContent();
        await client.PostAsync("/api/locations", firstContent);
        using var secondContent = BuildContent(new() { ["Name"] = "Otro nombre" });
        var second = await client.PostAsync("/api/locations", secondContent);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("CardLimit", "6", "cardLimit")]
    [InlineData("CardLimit", null, "cardLimit")]
    [InlineData("EnergyMode", "Inventada", "energyMode")]
    [InlineData("EnergyAmount", "4", "energyAmount")]
    [InlineData("Name", null, "name")]
    [InlineData("Description", null, "description")]
    [InlineData("EffectType", "Inventado", "effectType")]
    public async Task CreateLocation_InvalidField_ReturnsBadRequestIndicatingTheField(
        string name, string? value, string expectedField)
    {
        using var client = AdminClient($"loc-invalid-{name}-{value ?? "none"}");
        using var content = BuildContent(new() { [name] = value });

        var response = await client.PostAsync("/api/locations", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(expectedField, json.RootElement.GetProperty("field").GetString());
    }

    [Fact]
    public async Task CreateLocation_WithPortraitImage_IsRejected()
    {
        using var client = AdminClient("loc-portrait-001");
        using var content = BuildContent(width: 600, height: 840);

        var response = await client.PostAsync("/api/locations", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("invalid-image", json.RootElement.GetProperty("code").GetString());
    }

    private HttpClient AdminClient(string idempotencyKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        return client;
    }

    // Un valor null en overrides quita el campo del formulario.
    private static MultipartFormDataContent BuildContent(
        Dictionary<string, string?>? overrides = null,
        int width = 800,
        int height = 600)
    {
        var fields = new Dictionary<string, string?>
        {
            ["Name"] = "Gruta Volcánica",
            ["Description"] = "Una cueva caliente.",
            ["CardLimit"] = "3",
            ["EnergyMode"] = "Fija",
            ["EnergyAmount"] = "2"
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            fields[key] = value;

        var content = new MultipartFormDataContent();
        foreach (var (key, value) in fields)
        {
            if (value is not null)
                content.Add(new StringContent(value), key);
        }

        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var file = new ByteArrayContent(data.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "Image", "location.png");
        return content;
    }
}