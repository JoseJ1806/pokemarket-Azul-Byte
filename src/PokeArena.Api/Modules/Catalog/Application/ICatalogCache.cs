using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace PokeArena.Api.Modules.Catalog.Application;

public sealed class CatalogCacheOptions
{
    public int DefaultTtlSeconds { get; set; } = 30;
    public int MaximumTtlSeconds { get; set; } = 60;
}

public interface ICatalogCache
{
    Task<CatalogResponse?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(
        string key,
        Guid playerId,
        CatalogResponse response,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    Task InvalidateCatalogAsync(CancellationToken cancellationToken);

    Task InvalidatePlayerAsync(Guid playerId, CancellationToken cancellationToken);
}

public sealed class DistributedCatalogCache(
    IDistributedCache cache,
    ILogger<DistributedCatalogCache> logger,
    IConnectionMultiplexer? redis = null) : ICatalogCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, byte> MemoryKeys = new();
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> MemoryPlayerKeys = new();
    private const string RedisAllKeys = "catalog:__all_keys";
    private static string RedisPlayerKeys(Guid playerId) => $"catalog:__player_keys:{playerId}";

    public async Task<CatalogResponse?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken);
            if (cached is null)
                return null;

            return JsonSerializer.Deserialize<CatalogResponse>(cached, SerializerOptions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Degradación de caché; se consultará PostgreSQL.");
            return null;
        }
    }

    public async Task SetAsync(
        string key,
        Guid playerId,
        CatalogResponse response,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(response, SerializerOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                },
                cancellationToken);

            MemoryKeys[key] = 0;
            MemoryPlayerKeys.GetOrAdd(playerId, _ => new()).TryAdd(key, 0);

            if (redis is not null)
            {
                var database = redis.GetDatabase();
                await database.SetAddAsync(RedisAllKeys, key);
                await database.SetAddAsync(RedisPlayerKeys(playerId), key);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "No se pudo guardar la respuesta del catálogo en caché.");
        }
    }

    public async Task InvalidateCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (redis is not null)
        {
            var database = redis.GetDatabase();
            var keys = await database.SetMembersAsync(RedisAllKeys);
            var redisKeys = keys.Select(key => (RedisKey)key.ToString()).ToArray();
            if (redisKeys.Length > 0)
                await database.KeyDeleteAsync(redisKeys);
            await database.KeyDeleteAsync(RedisAllKeys);
            return;
        }

        foreach (var key in MemoryKeys.Keys)
            await cache.RemoveAsync(key, cancellationToken);
        MemoryKeys.Clear();
        MemoryPlayerKeys.Clear();
    }

    public async Task InvalidatePlayerAsync(Guid playerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (redis is not null)
        {
            var database = redis.GetDatabase();
            var indexKey = RedisPlayerKeys(playerId);
            var keys = await database.SetMembersAsync(indexKey);
            var redisKeys = keys.Select(key => (RedisKey)key.ToString()).ToArray();
            if (redisKeys.Length > 0)
                await database.KeyDeleteAsync(redisKeys);
            await database.KeyDeleteAsync(indexKey);
            return;
        }

        if (MemoryPlayerKeys.TryRemove(playerId, out var playerKeys))
        {
            foreach (var key in playerKeys.Keys)
            {
                await cache.RemoveAsync(key, cancellationToken);
                MemoryKeys.TryRemove(key, out _);
            }
        }
    }
}
