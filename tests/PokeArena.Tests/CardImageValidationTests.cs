using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SkiaSharp;

namespace PokeArena.Tests;

// Pruebas de los criterios de fallo de la imagen y de la autorización en POST /api/cards.
[Collection("Cards API")]
public class CardImageValidationTests
{
    private const int MaxBytes = 5 * 1024 * 1024;

    private readonly CardsApiFactory factory;

    public CardImageValidationTests(CardsApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task CreateCard_WithoutAdminRole_ReturnsForbidden()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "User");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "image-forbidden-001");

        using var content = BuildForm(PngBytes(600, 840), "image/png");
        var response = await client.PostAsync("/api/cards", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateCard_WithoutImage_ReturnsBadRequest()
    {
        using var client = AdminClient("image-missing-001");

        using var content = BuildForm(null, null);
        var response = await client.PostAsync("/api/cards", content);

        await AssertInvalid(response, "required-field", "image");
    }

    [Fact]
    public async Task CreateCard_WithUnsupportedFormat_ReturnsBadRequest()
    {
        using var client = AdminClient("image-format-001");

        using var content = BuildForm(PngBytes(600, 840), "image/gif");
        var response = await client.PostAsync("/api/cards", content);

        await AssertInvalid(response, "invalid-image", "image");
    }

    [Fact]
    public async Task CreateCard_WithBytesThatAreNotAnImage_ReturnsBadRequest()
    {
        using var client = AdminClient("image-corrupt-001");

        using var content = BuildForm("esto no es una imagen"u8.ToArray(), "image/png");
        var response = await client.PostAsync("/api/cards", content);

        await AssertInvalid(response, "invalid-image", "image");
    }

    [Fact]
    public async Task CreateCard_WithFileOverFiveMegabytes_ReturnsBadRequest()
    {
        using var client = AdminClient("image-size-001");

        using var content = BuildForm(new byte[MaxBytes + 1], "image/png");
        var response = await client.PostAsync("/api/cards", content);

        await AssertInvalid(response, "invalid-image", "image");
    }

    [Fact]
    public async Task CreateCard_WithWrongAspectRatio_ReturnsBadRequest()
    {
        using var client = AdminClient("image-ratio-001");

        // 700 x 700 cumple la resolución pero no la proporción 5:7.
        using var content = BuildForm(PngBytes(700, 700), "image/png");
        var response = await client.PostAsync("/api/cards", content);

        await AssertInvalid(response, "invalid-image", "image");
    }

    [Fact]
    public async Task CreateCard_WithResolutionBelowMinimum_ReturnsBadRequest()
    {
        using var client = AdminClient("image-resolution-001");

        // 300 x 420 tiene proporción 5:7 pero es menor que 600 x 840.
        using var content = BuildForm(PngBytes(300, 420), "image/png");
        var response = await client.PostAsync("/api/cards", content);

        await AssertInvalid(response, "invalid-image", "image");
    }

    [Fact]
    public async Task CreateCard_WithInvalidImage_DoesNotCreateTheCard()
    {
        using var client = AdminClient("image-nocreate-001");

        using var content = BuildForm(PngBytes(700, 700), "image/png");
        await client.PostAsync("/api/cards", content);

        Assert.Equal(0, await factory.CountCardsByIdempotencyKeyAsync("image-nocreate-001"));
    }

    private HttpClient AdminClient(string idempotencyKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        return client;
    }

    private static async Task AssertInvalid(HttpResponseMessage response, string code, string field)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(field, json.RootElement.GetProperty("field").GetString());
    }

    private static MultipartFormDataContent BuildForm(byte[]? image, string? contentType)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent("Carta de imagen"), "Name" },
            { new StringContent("3"), "Cost" },
            { new StringContent("5"), "Power" },
            { new StringContent("Agua"), "Type" },
            { new StringContent("Común"), "Rarity" },
            { new StringContent("HAB-000"), "AbilityCode" }
        };

        if (image is not null)
        {
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType!);
            content.Add(file, "Image", "card.bin");
        }

        return content;
    }

    private static byte[] PngBytes(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
