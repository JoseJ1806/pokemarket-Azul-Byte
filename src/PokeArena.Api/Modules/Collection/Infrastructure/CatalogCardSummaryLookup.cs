using PokeArena.Api.Modules.Catalog.Application;
using PokeArena.Api.Modules.Collection.Application;

namespace PokeArena.Api.Modules.Collection.Infrastructure;

// ADAPTADOR del puerto ICardSummaryLookup hacia el módulo Catálogo.
//
// Convierte el tipo de Catálogo (CatalogCardSummary) en el tipo propio de Colección
// (CardSummary), para que Colección no dependa de los tipos internos de otro módulo.
public sealed class CatalogCardSummaryLookup(ICardCatalogQuery catalog) : ICardSummaryLookup
{
    public async Task<IReadOnlyList<CardSummary>> GetByIdsAsync(
        IReadOnlyCollection<int> cardIds,
        CancellationToken cancellationToken)
    {
        var found = await catalog.GetSummariesAsync(cardIds, cancellationToken);
        return found
            .Select(card => new CardSummary(
                card.Id, card.Code, card.Name, card.Type, card.Rarity, card.ImageUrl, card.ThumbnailUrl))
            .ToList();
    }
}
