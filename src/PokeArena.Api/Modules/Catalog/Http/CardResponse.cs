using PokeArena.Api.Models;

namespace PokeArena.Api.Modules.Catalog.Http;

// Contrato JSON de una carta. Usa los mismos campos que cada resultado de la búsqueda
// y no expone la llave de idempotencia.
public sealed record CardResponse(
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
    string? AbilityCode,
    string? AbilityTrigger,
    string? AbilityEffect,
    string? AbilityAffectedType)
{
    public static CardResponse From(Card card) => new(
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
        card.AbilityCode,
        card.AbilityTrigger,
        card.AbilityEffect,
        card.AbilityAffectedType);
}
