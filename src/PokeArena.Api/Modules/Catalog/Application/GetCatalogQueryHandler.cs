using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PokeArena.Api.Data;
using PokeArena.Api.Models;
using PokeArena.Api.Modules.Catalog.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Catalog.Application;

public sealed record CatalogQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Type,
    string? Rarity,
    int? MinPower,
    int? MaxPower,
    string? AbilityCode,
    bool OnlyOwned,
    Guid PlayerId);

public sealed record CatalogCard(
    int Id,
    string Code,
    string Name,
    string Type,
    string Rarity,
    int Power,
    string AbilityCode,
    int QuantityOwned,
    bool IsOwned,
    string? ImageUrl);

public sealed record CatalogResponse(
    int Page,
    int PageSize,
    bool HasNextPage,
    bool HasPreviousPage,
    IReadOnlyList<CatalogCard> Items,
    string? NextCursor = null);

public sealed record CatalogQueryResult(CatalogQuery? Query, DomainError? Error);

public static class CatalogQueryParser
{
    public static CatalogQueryResult Parse(
        IQueryCollection query,
        Guid playerId)
    {
        var pageResult = ParseInt(query, "page", 1, 1, int.MaxValue);
        if (pageResult.Error is { } error)
            return new(null, error);

        var pageSizeResult = ParseInt(query, "pageSize", 20, 1, 100);
        if (pageSizeResult.Error is { } pageSizeError)
            return new(null, pageSizeError);

        var minPowerResult = ParseOptionalInt(query, "minPower", CardRules.MinPower, CardRules.MaxPower);
        if (minPowerResult.Error is { } minPowerError)
            return new(null, minPowerError);

        var maxPowerResult = ParseOptionalInt(query, "maxPower", CardRules.MinPower, CardRules.MaxPower);
        if (maxPowerResult.Error is { } maxPowerError)
            return new(null, maxPowerError);

        if (minPowerResult.Value is { } minPower &&
            maxPowerResult.Value is { } maxPower &&
            minPower > maxPower)
        {
            return new(null, DomainError.Validation(
                "invalid-range",
                "minPower no puede ser mayor que maxPower.",
                "minPower"));
        }

        var type = OptionalText(query, "type");
        if (type is not null && !CardRules.TypePrefixes.ContainsKey(type))
        {
            return new(null, DomainError.Validation(
                "invalid-enum",
                $"type debe ser uno de: {string.Join(", ", CardRules.TypePrefixes.Keys)}.",
                "type"));
        }

        var rarity = OptionalText(query, "rarity");
        if (rarity is not null && !CardRules.Rarities.Contains(rarity))
        {
            return new(null, DomainError.Validation(
                "invalid-enum",
                $"rarity debe ser uno de: {string.Join(", ", CardRules.Rarities)}.",
                "rarity"));
        }

        var abilityCode = OptionalText(query, "abilityCode");
        if (string.Equals(abilityCode, "Sin habilidad", StringComparison.OrdinalIgnoreCase))
            abilityCode = CardRules.NoAbilityCode;

        var onlyOwnedResult = ParseBoolean(query, "onlyOwned", false);
        if (onlyOwnedResult.Error is { } onlyOwnedError)
            return new(null, onlyOwnedError);

        return new(
            new CatalogQuery(
                pageResult.Value,
                pageSizeResult.Value,
                OptionalText(query, "search"),
                type,
                rarity,
                minPowerResult.Value,
                maxPowerResult.Value,
                abilityCode,
                onlyOwnedResult.Value,
                playerId),
            null);
    }

    private static string? OptionalText(IQueryCollection query, string name)
    {
        var value = query[name].ToString().Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static (int Value, DomainError? Error) ParseInt(
        IQueryCollection query,
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var raw = query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return (defaultValue, null);

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return (default, DomainError.Validation("invalid-parameter", $"{name} debe ser un entero.", name));

        return value < minimum || value > maximum
            ? (default, DomainError.Validation(
                "invalid-range",
                $"{name} debe estar entre {minimum} y {maximum}.",
                name))
            : (value, null);
    }

    private static (int? Value, DomainError? Error) ParseOptionalInt(
        IQueryCollection query,
        string name,
        int minimum,
        int maximum)
    {
        var raw = query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return (null, DomainError.Validation("invalid-parameter", $"{name} debe ser un entero.", name));

        return value < minimum || value > maximum
            ? (null, DomainError.Validation(
                "invalid-range",
                $"{name} debe estar entre {minimum} y {maximum}.",
                name))
            : (value, null);
    }

    private static (bool Value, DomainError? Error) ParseBoolean(
        IQueryCollection query,
        string name,
        bool defaultValue)
    {
        var raw = query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return (defaultValue, null);

        return bool.TryParse(raw, out var value)
            ? (value, null)
            : (default, DomainError.Validation(
                "invalid-boolean",
                $"{name} debe ser true o false.",
                name));
    }
}

public interface ICatalogService
{
    Task<CatalogResponse> ExecuteAsync(CatalogQuery query, CancellationToken cancellationToken);
}

public sealed class GetCatalogQueryHandler(PokeArenaDbContext db) : ICatalogService
{
    public async Task<CatalogResponse> ExecuteAsync(
        CatalogQuery query,
        CancellationToken cancellationToken)
    {
        var cardsQuery = db.Cards
            .AsNoTracking()
            .Where(card => card.IsAvailable);

        if (query.Search is not null)
        {
            var search = NormalizeForLike(query.Search);
            cardsQuery = cardsQuery.Where(card =>
                EF.Functions.Unaccent(card.Name) != null &&
                EF.Functions.ILike(EF.Functions.Unaccent(card.Name), $"%{search}%"));
        }

        if (query.Type is not null)
            cardsQuery = cardsQuery.Where(card => card.Type == query.Type);

        if (query.Rarity is not null)
            cardsQuery = cardsQuery.Where(card => card.Rarity == query.Rarity);

        if (query.MinPower is not null)
            cardsQuery = cardsQuery.Where(card => card.Power >= query.MinPower.Value);

        if (query.MaxPower is not null)
            cardsQuery = cardsQuery.Where(card => card.Power <= query.MaxPower.Value);

        if (query.AbilityCode == CardRules.NoAbilityCode)
        {
            cardsQuery = cardsQuery.Where(card =>
                card.AbilityCode == null ||
                card.AbilityCode == string.Empty ||
                card.AbilityCode == CardRules.NoAbilityCode);
        }
        else if (query.AbilityCode is not null)
        {
            cardsQuery = cardsQuery.Where(card => card.AbilityCode == query.AbilityCode);
        }

        if (query.OnlyOwned)
        {
            cardsQuery = cardsQuery.Where(card =>
                db.CardCopies.Any(copy =>
                    copy.PlayerId == query.PlayerId &&
                    copy.CardId == card.Id));
        }

        var projectedCards = await cardsQuery
            .OrderBy(card => card.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize + 1)
            .Select(card => new CatalogCardData(
                card.Id,
                card.Code,
                card.Name,
                card.Type,
                card.Rarity,
                card.Power,
                card.AbilityCode,
                card.ImageUrl))
            .ToListAsync(cancellationToken);

        var hasNextPage = projectedCards.Count > query.PageSize;
        if (hasNextPage)
            projectedCards.RemoveAt(projectedCards.Count - 1);

        var cardIds = projectedCards.Select(card => card.Id).ToArray();
        var ownedCounts = cardIds.Length == 0
            ? new Dictionary<int, int>()
            : await db.CardCopies
                .AsNoTracking()
                .Where(copy => copy.PlayerId == query.PlayerId && cardIds.Contains(copy.CardId))
                .GroupBy(copy => copy.CardId)
                .Select(group => new { CardId = group.Key, Quantity = group.Count() })
                .ToDictionaryAsync(item => item.CardId, item => item.Quantity, cancellationToken);

        var items = projectedCards
            .Select(card =>
            {
                var quantity = ownedCounts.GetValueOrDefault(card.Id);
                return new CatalogCard(
                    card.Id,
                    card.Code,
                    card.Name,
                    card.Type,
                    card.Rarity,
                    card.Power,
                    card.AbilityCode ?? CardRules.NoAbilityCode,
                    quantity,
                    quantity > 0,
                    card.ImageUrl);
            })
            .Where(card => !query.OnlyOwned || card.IsOwned)
            .ToList();

        return new CatalogResponse(
            query.Page,
            query.PageSize,
            hasNextPage,
            query.Page > 1,
            items,
            hasNextPage ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"{query.Page + 1}")) : null);
    }

    private static string NormalizeForLike(string value) =>
        value.Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Aggregate(new StringBuilder(), (builder, character) => builder.Append(character))
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace("%", "\\%")
            .Replace("_", "\\_")
            .Replace("\\", "\\\\");

    private sealed record CatalogCardData(
        int Id,
        string Code,
        string Name,
        string Type,
        string Rarity,
        int Power,
        string? AbilityCode,
        string? ImageUrl);
}
