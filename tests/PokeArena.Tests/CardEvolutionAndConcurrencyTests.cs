using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PokeArena.Api.Data;
using PokeArena.Api.Models;
using SkiaSharp;

namespace PokeArena.Tests;

// Pruebas de ciclos de evolución y de creación simultánea de cartas.
// Cada prueba usa un tipo de carta distinto para no interferir con la numeración de las demás.
[Collection("Cards API")]
public class CardEvolutionAndConcurrencyTests
{
    private readonly CardsApiFactory factory;

    public CardEvolutionAndConcurrencyTests(CardsApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task CreateCard_EvolutionPointingToItself_ReturnsEvolutionCycle()
    {
        var nextCode = await NextCodeAsync("PSI");
        await AddAbilityAsync("TST-EVO-SELF", isEvolution: true, targetCardCode: nextCode);

        using var client = AdminClient("evolution-self-001");
        using var content = BuildForm("Psíquico", "TST-EVO-SELF");
        var response = await client.PostAsync("/api/cards", content);

        await AssertInvalid(response, "evolution-cycle");
    }

    [Fact]
    public async Task CreateCard_EvolutionChainThatReturnsToNewCard_ReturnsEvolutionCycle()
    {
        var nextCode = await NextCodeAsync("PSI");

        // CYC-A evoluciona hacia la carta que se está creando, y la nueva evoluciona hacia CYC-A.
        await AddAbilityAsync("TST-EVO-A", isEvolution: true, targetCardCode: nextCode);
        await AddCardAsync("CYC-A", "TST-EVO-A");
        await AddAbilityAsync("TST-EVO-B", isEvolution: true, targetCardCode: "CYC-A");

        using var client = AdminClient("evolution-chain-001");
        using var content = BuildForm("Psíquico", "TST-EVO-B");
        var response = await client.PostAsync("/api/cards", content);

        await AssertInvalid(response, "evolution-cycle");
        Assert.Equal(0, await factory.CountCardsByIdempotencyKeyAsync("evolution-chain-001"));
    }

    [Fact]
    public async Task CreateCard_EvolutionWithoutTarget_ReturnsInvalidEvolution()
    {
        await AddAbilityAsync("TST-EVO-NOTARGET", isEvolution: true, targetCardCode: null);

        using var client = AdminClient("evolution-notarget-001");
        using var content = BuildForm("Psíquico", "TST-EVO-NOTARGET");
        var response = await client.PostAsync("/api/cards", content);

        await AssertInvalid(response, "invalid-evolution");
    }

    [Fact]
    public async Task CreateCard_EvolutionToExistingCardWithoutCycle_IsCreated()
    {
        await AddCardAsync("EVO-BASE", abilityCode: null);
        await AddAbilityAsync("TST-EVO-OK", isEvolution: true, targetCardCode: "EVO-BASE");

        using var client = AdminClient("evolution-ok-001");
        using var content = BuildForm("Eléctrico", "TST-EVO-OK");
        var response = await client.PostAsync("/api/cards", content);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateCard_SimultaneousRequestsOfSameType_GetDifferentCodes()
    {
        const int requests = 6;

        var responses = await Task.WhenAll(Enumerable.Range(1, requests).Select(async index =>
        {
            using var client = AdminClient($"concurrent-lucha-{index:000}");
            using var content = BuildForm("Lucha", "HAB-000");
            var response = await client.PostAsync("/api/cards", content);
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        }));

        Assert.All(responses, r => Assert.True(r.StatusCode == HttpStatusCode.Created, r.Body));

        var codes = responses
            .Select(r =>
            {
                using var json = JsonDocument.Parse(r.Body);
                return json.RootElement.GetProperty("code").GetString()!;
            })
            .ToList();

        Assert.Equal(requests, codes.Distinct().Count());
        Assert.All(codes, code => Assert.StartsWith("PKA-LUC-", code));

        // Los códigos deben ser consecutivos, sin saltos ni repetidos.
        var numbers = codes.Select(code => int.Parse(code[^3..])).Order().ToList();
        Assert.Equal(Enumerable.Range(numbers[0], requests), numbers);
    }

    [Fact]
    public async Task CreateCard_SimultaneousRetriesWithSameKey_CreateOnlyOneCard()
    {
        const int requests = 4;

        var responses = await Task.WhenAll(Enumerable.Range(1, requests).Select(async _ =>
        {
            using var client = AdminClient("concurrent-same-key-001");
            using var content = BuildForm("Planta", "HAB-000");
            var response = await client.PostAsync("/api/cards", content);
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        }));

        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.Body));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, await factory.CountCardsByIdempotencyKeyAsync("concurrent-same-key-001"));
    }

    private HttpClient AdminClient(string idempotencyKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        return client;
    }

    private async Task<string> NextCodeAsync(string typePrefix)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        var prefix = $"PKA-{typePrefix}-";
        var codes = await db.Cards
            .Where(card => card.Code.StartsWith(prefix))
            .Select(card => card.Code)
            .ToListAsync();
        var highest = codes.Select(code => int.Parse(code[prefix.Length..])).DefaultIfEmpty(0).Max();
        return $"{prefix}{highest + 1:000}";
    }

    private async Task AddAbilityAsync(string code, bool isEvolution, string? targetCardCode)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        db.Abilities.Add(new Ability
        {
            Code = code,
            Name = $"Habilidad {code}",
            IsEvolution = isEvolution,
            TargetCardCode = targetCardCode
        });
        await db.SaveChangesAsync();
    }

    private async Task AddCardAsync(string code, string? abilityCode)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PokeArenaDbContext>();
        db.Cards.Add(new Card
        {
            Code = code,
            Name = $"Carta {code}",
            ImageStatus = "Lista",
            Type = "Psíquico",
            Rarity = "Común",
            AbilityCode = abilityCode
        });
        await db.SaveChangesAsync();
    }

    private static async Task AssertInvalid(HttpResponseMessage response, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Equal("abilityCode", json.RootElement.GetProperty("field").GetString());
    }

    private static MultipartFormDataContent BuildForm(string type, string abilityCode)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent("Carta de prueba"), "Name" },
            { new StringContent("3"), "Cost" },
            { new StringContent("5"), "Power" },
            { new StringContent(type), "Type" },
            { new StringContent("Común"), "Rarity" },
            { new StringContent(abilityCode), "AbilityCode" }
        };

        using var bitmap = new SKBitmap(600, 840);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var file = new ByteArrayContent(data.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "Image", "card.png");
        return content;
    }
}
