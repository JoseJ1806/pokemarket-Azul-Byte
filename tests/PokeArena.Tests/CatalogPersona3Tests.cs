using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
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
        var cache = new TestDistributedCache();
        var decorated = new CountingCatalogService(
            new CatalogResponse(1, 20, false, false, [new CatalogCard(1, "X", "X", "Agua", "Rara", 5, "HAB-000", 1, true, "https://img")]));
        var service = new CachedCatalogService(
            decorated,
            cache,
            NullLogger<CachedCatalogService>.Instance);

        var response = await service.ExecuteAsync(Query(), CancellationToken.None);
        var second = await service.ExecuteAsync(Query(), CancellationToken.None);

        Assert.Single(response.Items);
        Assert.Single(second.Items);
        Assert.Equal(1, decorated.Calls);
    }

    [Fact]
    public async Task Cache_MissCallsDecoratedServiceAndStoresResponse()
    {
        var cache = new TestDistributedCache();
        var expected = new CatalogResponse(
            1, 20, false, false,
            [new CatalogCard(1, "X", "X", "Agua", "Rara", 5, "HAB-000", 1, true, "https://img")]);
        var decorated = new CountingCatalogService(expected);
        var service = new CachedCatalogService(
            decorated,
            cache,
            NullLogger<CachedCatalogService>.Instance);

        var response = await service.ExecuteAsync(Query(), CancellationToken.None);
        var second = await service.ExecuteAsync(Query(), CancellationToken.None);

        Assert.Single(response.Items);
        Assert.Single(second.Items);
        Assert.Equal(1, decorated.Calls);
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

    private sealed class TestDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> values = new();

            public byte[]? Get(string key) =>
                values.TryGetValue(key, out var value) ? value : null;

            public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
                Task.FromResult(Get(key));

            public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
                values[key] = value;

            public Task SetAsync(
                string key,
                byte[] value,
                DistributedCacheEntryOptions options,
                CancellationToken token = default)
            {
                Set(key, value, options);
                return Task.CompletedTask;
            }

            public void Refresh(string key)
            {
            }

            public Task RefreshAsync(string key, CancellationToken token = default) =>
                Task.CompletedTask;

            public void Remove(string key) => values.TryRemove(key, out _);

            public Task RemoveAsync(string key, CancellationToken token = default)
            {
                Remove(key);
                return Task.CompletedTask;
        }
    }
}
