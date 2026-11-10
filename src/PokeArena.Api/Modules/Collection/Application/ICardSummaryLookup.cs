namespace PokeArena.Api.Modules.Collection.Application;

// Lo que Colección necesita saber de una carta para mostrarla: su "tarjeta de presentación".
// Es el tipo PROPIO de Colección; el adaptador lo arma a partir de lo que entrega Catálogo.
public sealed record CardSummary(
    int Id,
    string Code,
    string Name,
    string Type,
    string Rarity,
    string? ImageUrl,
    string? ThumbnailUrl);

// PUERTO: DATOS DE PRESENTACIÓN DE CARTAS.
//
// Qué es: la pregunta "dame el nombre e imagen de estas cartas". La necesita la consulta
// de la colección del jugador (GET /api/me/collection), que muestra las cartas.
public interface ICardSummaryLookup
{
    // Devuelve los datos de las cartas pedidas. Si algún identificador no existe, no
    // aparece en el resultado.
    Task<IReadOnlyList<CardSummary>> GetByIdsAsync(
        IReadOnlyCollection<int> cardIds,
        CancellationToken cancellationToken);
}
