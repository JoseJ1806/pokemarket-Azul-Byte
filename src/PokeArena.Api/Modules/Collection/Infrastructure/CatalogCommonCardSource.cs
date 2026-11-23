using PokeArena.Api.Modules.Catalog.Application;
using PokeArena.Api.Modules.Collection.Application;

namespace PokeArena.Api.Modules.Collection.Infrastructure;

// ADAPTADOR del puerto ICommonCardSource hacia el módulo Catálogo.
//
// Es el ÚNICO punto por donde Colección pregunta por las cartas Comunes. Usa la interfaz
// pública de Catálogo (ICardCatalogQuery) y no sus tablas, igual que Ubicaciones usa
// CatalogCardTypeLookup para saber si un tipo de carta existe. Quien sabe qué es "Común"
// y qué es "disponible" es Catálogo; este adaptador solo reenvía la pregunta.
public sealed class CatalogCommonCardSource(ICardCatalogQuery catalog) : ICommonCardSource
{
    public Task<IReadOnlyList<int>> GetAvailableCommonCardIdsAsync(CancellationToken cancellationToken) =>
        catalog.GetAvailableCommonCardIdsAsync(cancellationToken);
}
