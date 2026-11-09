using Microsoft.EntityFrameworkCore;
using PokeArena.Api.Data;
using PokeArena.Api.Modules.Catalog.Application;
using PokeArena.Api.Modules.Catalog.Domain;

namespace PokeArena.Api.Modules.Catalog.Infrastructure;

// ADAPTADOR de la interfaz pública de consulta del catálogo, con Entity Framework.
// Aquí vive el conocimiento de que "Común" es CardRules.CommonRarity y de que una carta
// está "disponible" cuando Card.IsAvailable es verdadero.
public sealed class EfCardCatalogQuery(PokeArenaDbContext db) : ICardCatalogQuery
{
    // AsNoTracking: solo se lee. Select de una sola columna: la consulta trae únicamente
    // los identificadores, no las cartas completas.
    public async Task<IReadOnlyList<int>> GetAvailableCommonCardIdsAsync(CancellationToken cancellationToken) =>
        await db.Cards.AsNoTracking()
            .Where(card => card.IsAvailable && card.Rarity == CardRules.CommonRarity)
            .Select(card => card.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CatalogCardSummary>> GetSummariesAsync(
        IReadOnlyCollection<int> cardIds,
        CancellationToken cancellationToken)
    {
        // Una lista concreta para que EF la traduzca a "WHERE Id IN (...)".
        var ids = cardIds.ToList();
        return await db.Cards.AsNoTracking()
            .Where(card => ids.Contains(card.Id))
            .Select(card => new CatalogCardSummary(
                card.Id, card.Code, card.Name, card.Type, card.Rarity, card.ImageUrl, card.ThumbnailUrl))
            .ToListAsync(cancellationToken);
    }
}
