using PokeArena.Api.Models;

namespace PokeArena.Api.Modules.Catalog.Http;

// Ficha completa de una carta: los datos de la carta y el detalle de su habilidad.
public sealed record CardDetailResponse(
    int Id,
    string Code,
    string Name,
    string? ImageUrl,
    string? ThumbnailUrl,
    string ImageStatus,
    bool IsAvailable,
    int Cost,
    int Power,
    string Type,
    string Rarity,
    AbilityDetailResponse? Ability)
{
    public static CardDetailResponse From(Card card, Ability? ability) => new(
        card.Id,
        card.Code,
        card.Name,
        card.ImageUrl,
        card.ThumbnailUrl,
        card.ImageStatus,
        card.IsAvailable,
        card.Cost,
        card.Power,
        card.Type,
        card.Rarity,
        ability is null ? null : AbilityDetailResponse.From(ability));
}

public sealed record AbilityDetailResponse(
    string Code,
    string Name,
    string? Trigger,
    string? Effect,
    string? AffectedType,
    bool IsEvolution,
    string? TargetCardCode)
{
    public static AbilityDetailResponse From(Ability ability) => new(
        ability.Code,
        ability.Name,
        ability.Trigger,
        ability.Effect,
        ability.AffectedType,
        ability.IsEvolution,
        ability.TargetCardCode);
}
