using PokeArena.Api.Modules.Collection.Application;

namespace PokeArena.Api.Modules.Collection.Http;

// Una carta de la colección, tal como la recibe el cliente.
// Copies: cuántas copias de esa carta tiene el jugador (la respuesta viene agrupada).
public sealed record CollectionCardResponse(
    int CardId,
    string Code,
    string Name,
    string Type,
    string Rarity,
    string? ImageUrl,
    string? ThumbnailUrl,
    int Copies);

// RESPUESTA de GET /api/me/collection: el saldo de cubos y las cartas del jugador.
public sealed record CollectionResponse(int Cubes, IReadOnlyList<CollectionCardResponse> Cards)
{
    public static CollectionResponse From(CollectionView view) =>
        new(
            view.Cubes,
            view.Cards
                .Select(item => new CollectionCardResponse(
                    item.Card.Id,
                    item.Card.Code,
                    item.Card.Name,
                    item.Card.Type,
                    item.Card.Rarity,
                    item.Card.ImageUrl,
                    item.Card.ThumbnailUrl,
                    item.Copies))
                .ToList());
}
