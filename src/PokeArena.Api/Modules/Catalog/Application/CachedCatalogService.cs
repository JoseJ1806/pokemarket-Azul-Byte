using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace PokeArena.Api.Modules.Catalog.Application;

public sealed class CachedCatalogService(
    ICatalogService decorated,
    ICatalogCache cache,
    IOptions<CatalogCacheOptions> options) : ICatalogService
{
    private readonly TimeSpan defaultTtl = TimeSpan.FromSeconds(
        Math.Clamp(options.Value.DefaultTtlSeconds, 1, options.Value.MaximumTtlSeconds));

    public async Task<CatalogResponse> ExecuteAsync(
        CatalogQuery query,
        CancellationToken cancellationToken)
    {
        var key = BuildKey(query);
        var cached = await cache.GetAsync(key, cancellationToken);
        if (cached is not null)
            return cached;

        var response = await decorated.ExecuteAsync(query, cancellationToken);
        await cache.SetAsync(key, query.PlayerId, response, defaultTtl, cancellationToken);
        return response;
    }

    private static string BuildKey(CatalogQuery query)
    {
        var canonicalFilters = string.Join(
            "|",
            "catalog-v1",
            query.PlayerId,
            query.PageSize,
            NormalizeKeyValue(query.Search),
            NormalizeKeyValue(query.Type),
            NormalizeKeyValue(query.Rarity),
            query.MinPower?.ToString() ?? string.Empty,
            query.MaxPower?.ToString() ?? string.Empty,
            NormalizeKeyValue(query.AbilityCode),
            query.OnlyOwned);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalFilters)));
        return $"catalog:{hash}:{query.Page}";
    }

    private static string NormalizeKeyValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                normalized.Append(character);
        }

        return normalized.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}
