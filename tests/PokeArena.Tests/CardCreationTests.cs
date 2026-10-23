using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace PokeArena.Tests;

// Pruebas del MVP de Crear Carta usando el mismo host y PostgreSQL aislado
// que CardsSearchTests.
[Collection("Cards API")]
public class CardCreationTests
{
    private readonly CardsApiFactory factory;

    public CardCreationTests(CardsApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task CreateCard_WithoutCredentials_ReturnsUnauthorized()
    {
        using var content = CreateMultipartContent();
        var response = await factory.CreateClient().PostAsync("/api/cards", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateCard_ValidRequestCreatesImagesAndCode()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "create-card-001");
        using var content = CreateMultipartContent();
        content.Add(new StringContent("Carta MVP"), "Name");
        content.Add(new StringContent("3"), "Cost");
        content.Add(new StringContent("-10"), "Power");
        content.Add(new StringContent("Agua"), "Type");
        content.Add(new StringContent("Rara"), "Rarity");
        content.Add(new StringContent("HAB-000"), "AbilityCode");

        var response = await client.PostAsync("/api/cards", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PKA-AGU-", body);
        Assert.Contains("-small.webp", body);
    }

    [Fact]
    public async Task CreateCard_RejectsInvalidPowerAndMissingAbility()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

        using var invalidPower = CreateMultipartContent();
        invalidPower.Add(new StringContent("Carta inválida"), "Name");
        invalidPower.Add(new StringContent("3"), "Cost");
        invalidPower.Add(new StringContent("21"), "Power");
        invalidPower.Add(new StringContent("Agua"), "Type");
        invalidPower.Add(new StringContent("Rara"), "Rarity");
        invalidPower.Add(new StringContent("HAB-000"), "AbilityCode");
        var powerResponse = await client.PostAsync("/api/cards", invalidPower);
        Assert.Equal(HttpStatusCode.BadRequest, powerResponse.StatusCode);

        using var missingAbility = CreateMultipartContent();
        missingAbility.Add(new StringContent("Carta inválida"), "Name");
        missingAbility.Add(new StringContent("3"), "Cost");
        missingAbility.Add(new StringContent("10"), "Power");
        missingAbility.Add(new StringContent("Agua"), "Type");
        missingAbility.Add(new StringContent("Rara"), "Rarity");
        var abilityResponse = await client.PostAsync("/api/cards", missingAbility);
        Assert.Equal(HttpStatusCode.BadRequest, abilityResponse.StatusCode);
    }

    [Fact]
    public async Task CreateCard_RetryWithSameIdempotencyKeyDoesNotDuplicate()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

        using var firstContent = CreateMultipartContent();
        AddValidFields(firstContent);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "retry-card-001");
        var first = await client.PostAsync("/api/cards", firstContent);

        using var secondContent = CreateMultipartContent();
        AddValidFields(secondContent);
        var second = await client.PostAsync("/api/cards", secondContent);

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.True(first.StatusCode == HttpStatusCode.Created, firstBody);
        Assert.True(second.StatusCode == HttpStatusCode.OK, secondBody);

        using var firstJson = JsonDocument.Parse(firstBody);
        using var secondJson = JsonDocument.Parse(secondBody);
        Assert.Equal(
            firstJson.RootElement.GetProperty("code").GetString(),
            secondJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, await factory.CountCardsByIdempotencyKeyAsync("retry-card-001"));
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

    private static void AddValidFields(MultipartFormDataContent content)
    {
        content.Add(new StringContent("Carta repetible"), "Name");
        content.Add(new StringContent("2"), "Cost");
        content.Add(new StringContent("10"), "Power");
        content.Add(new StringContent("Agua"), "Type");
        content.Add(new StringContent("Común"), "Rarity");
        content.Add(new StringContent("HAB-000"), "AbilityCode");
    }
}

[CollectionDefinition("Cards API")]
public sealed class CardsApiCollection : ICollectionFixture<CardsApiFactory>
{
}