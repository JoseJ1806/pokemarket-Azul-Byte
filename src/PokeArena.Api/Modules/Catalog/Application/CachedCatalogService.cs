using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace PokeArena.Api.Modules.Catalog.Application;

public sealed class CachedCatalogService(
    ICatalogService decorated,
    IDistributedCache cache,
    ILogger<CachedCatalogService> logger) : ICatalogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<CatalogResponse> ExecuteAsync(
        CatalogQuery query,
        CancellationToken cancellationToken)
    {
        var key = BuildKey(query);
        try
        {
            var cached = await cache.GetStringAsync(key, cancellationToken);
            if (cached is not null)
                return JsonSerializer.Deserialize<CatalogResponse>(cached, SerializerOptions)
                    ?? throw new InvalidOperationException("La caché devolvió un catálogo vacío.");
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(exception, "Timeout leyendo la caché del catálogo; se consulta PostgreSQL.");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Caché del catálogo no disponible; se consulta PostgreSQL.");
        }

        var response = await decorated.ExecuteAsync(query, cancellationToken);

        try
        {
            await cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(response, SerializerOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                },
                cancellationToken);
        }
        catch (TimeoutException exception)
        {
            logger.LogWarning(exception, "Timeout escribiendo la caché del catálogo.");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "No se pudo actualizar la caché del catálogo.");
        }

        return response;
    }

    private static string BuildKey(CatalogQuery query)
    {
        var canonicalFilters = string.Join(
            "|",
            "catalog-v1",
            query.PlayerId,
            query.PageSize,
            query.Search ?? string.Empty,
            query.Type ?? string.Empty,
            query.Rarity ?? string.Empty,
            query.MinPower?.ToString() ?? string.Empty,
            query.MaxPower?.ToString() ?? string.Empty,
            query.AbilityCode ?? string.Empty,
            query.OnlyOwned);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalFilters)));
        return $"catalog:{hash}:{query.Page}";
    }
}
