using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PokeArena.Api.Modules.Catalog.Application;

namespace PokeArena.Tests;

public sealed class CatalogPersona3Tests
{
    [Fact]
    public void Parser_AcceptsCombinedFiltersAndMapsNoAbility()
    {
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["search"] = "Pikachu",
            ["type"] = "Agua",
            ["rarity"] = "Rara",
            ["minPower"] = "5",
            ["maxPower"] = "20",
            ["abilityCode"] = "Sin habilidad",
            ["onlyOwned"] = "true",
            ["page"] = "2",
            ["pageSize"] = "50"
        });

        var result = CatalogQueryParser.Parse(query, Guid.NewGuid());

        Assert.Null(result.Error);
        Assert.NotNull(result.Query);
        Assert.Equal("HAB-000", result.Query.AbilityCode);
        Assert.True(result.Query.OnlyOwned);
        Assert.Equal(2, result.Query.Page);
        Assert.Equal(50, result.Query.PageSize);
    }

    [Theory]
    [InlineData("minPower", "21")]
    [InlineData("maxPower", "-21")]
    [InlineData("page", "0")]
    [InlineData("page", "-1")]
    [InlineData("pageSize", "0")]
    [InlineData("pageSize", "101")]
    public void Parser_RejectsInvalidNumericParameters(string name, string value)
    {
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            [name] = value
        });

        var result = CatalogQueryParser.Parse(query, Guid.NewGuid());

        Assert.NotNull(result.Error);
        Assert.Equal("invalid-range", result.Error.Code);
    }

    [Fact]
    public void Parser_RejectsReversedPowerRangeAndInvalidEnums()
    {
        var reversed = CatalogQueryParser.Parse(
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["minPower"] = "10",
                ["maxPower"] = "5"
            }),
            Guid.NewGuid());
        var invalidType = CatalogQueryParser.Parse(
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["type"] = "Dragon"
            }),
            Guid.NewGuid());

        Assert.Equal("invalid-range", reversed.Error?.Code);
        Assert.Equal("invalid-enum", invalidType.Error?.Code);
    }

    [Fact]
    public void Parser_ReturnsInvalidBoolean()
    {
        var result = CatalogQueryParser.Parse(
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["onlyOwned"] = "yes"
            }),
            Guid.NewGuid());

        Assert.Equal("invalid-boolean", result.Error?.Code);
    }

    [Fact]
    public async Task Cache_ReturnsHitWithoutCallingDecoratedService()
    {
        var cache = new TestCatalogCache();
        var decorated = new CountingCatalogService(
            new CatalogResponse(1, 20, false, false, [new CatalogCard(1, "X", "X", "Agua", "Rara", 5, "HAB-000", 1, true, "https://img")]));
        var service = new CachedCatalogService(
            decorated,
            cache,
            Options.Create(new CatalogCacheOptions()));

        var response = await service.ExecuteAsync(Query(), CancellationToken.None);
        var second = await service.ExecuteAsync(Query(), CancellationToken.None);

        Assert.Single(response.Items);
        Assert.Single(second.Items);
        Assert.Equal(1, decorated.Calls);
    }

    [Fact]
    public async Task Cache_MissCallsDecoratedServiceAndStoresResponse()
    {
        var cache = new TestCatalogCache();
        var expected = new CatalogResponse(
            1, 20, false, false,
            [new CatalogCard(1, "X", "X", "Agua", "Rara", 5, "HAB-000", 1, true, "https://img")]);
        var decorated = new CountingCatalogService(expected);
        var service = new CachedCatalogService(
            decorated,
            cache,
            Options.Create(new CatalogCacheOptions()));

        var response = await service.ExecuteAsync(Query(), CancellationToken.None);
        var second = await service.ExecuteAsync(Query(), CancellationToken.None);

        Assert.Single(response.Items);
        Assert.Single(second.Items);
        Assert.Equal(1, decorated.Calls);
    }

    [Fact]
    public async Task Cache_UsesConfiguredThirtySecondTtl()
    {
        var cache = new TestCatalogCache();
        var decorated = new CountingCatalogService(
            new CatalogResponse(1, 20, false, false, []));
        var service = new CachedCatalogService(
            decorated,
            cache,
            Options.Create(new CatalogCacheOptions
            {
                DefaultTtlSeconds = 30,
                MaximumTtlSeconds = 60
            }));

        await service.ExecuteAsync(Query(), CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(30), cache.LastTtl);
    }

    [Fact]
    public async Task Cache_CatalogInvalidationCausesNextReadToMiss()
    {
        var cache = new TestCatalogCache();
        var decorated = new CountingCatalogService(
            new CatalogResponse(1, 20, false, false, []));
        var service = new CachedCatalogService(
            decorated,
            cache,
            Options.Create(new CatalogCacheOptions()));

        await service.ExecuteAsync(Query(), CancellationToken.None);
        await cache.InvalidateCatalogAsync(CancellationToken.None);
        await service.ExecuteAsync(Query(), CancellationToken.None);

        Assert.Equal(2, decorated.Calls);
    }

    private static CatalogQuery Query() =>
        new(1, 20, "Pikachu", null, null, null, null, null, false, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

    private sealed class CountingCatalogService(CatalogResponse response) : ICatalogService
    {
        public int Calls { get; private set; }

        public Task<CatalogResponse> ExecuteAsync(CatalogQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(response);
        }
    }

    private sealed class TestCatalogCache : ICatalogCache
    {
        private readonly Dictionary<string, CatalogResponse> values = new();
        public TimeSpan? LastTtl { get; private set; }

        public Task<CatalogResponse?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(values.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(
            string key,
            Guid playerId,
            CatalogResponse response,
            TimeSpan ttl,
            CancellationToken cancellationToken)
        {
            values[key] = response;
            LastTtl = ttl;
            return Task.CompletedTask;
        }

        public Task InvalidateCatalogAsync(CancellationToken cancellationToken)
        {
            values.Clear();
            return Task.CompletedTask;
        }

        public Task InvalidatePlayerAsync(Guid playerId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
