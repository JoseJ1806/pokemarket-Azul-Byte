namespace PokeArena.Api.Modules.Catalog.Application;

// Datos de presentación de una carta que Catálogo ofrece a otros módulos.
public sealed record CatalogCardSummary(
    int Id,
    string Code,
    string Name,
    string Type,
    string Rarity,
    string? ImageUrl,
    string? ThumbnailUrl);

// INTERFAZ PÚBLICA DE CONSULTA DEL CATÁLOGO.
//
// Es la forma en que OTROS módulos (hoy, Colección) le piden datos al catálogo sin leer
// sus tablas (driver D-04). El módulo Catálogo es quien decide qué significa "Común" y
// qué significa "disponible": quien pregunta solo recibe el resultado.
public interface ICardCatalogQuery
{
    // Identificadores de las cartas de rareza Común que están DISPONIBLES (con ambas
    // imágenes listas). Solo identificadores: con unas 10.000 cartas, no se cargan entidades.
    Task<IReadOnlyList<int>> GetAvailableCommonCardIdsAsync(CancellationToken cancellationToken);

    // Datos de presentación de las cartas pedidas. Los identificadores que no existan
    // no aparecen en el resultado.
    Task<IReadOnlyList<CatalogCardSummary>> GetSummariesAsync(
        IReadOnlyCollection<int> cardIds,
        CancellationToken cancellationToken);
}
