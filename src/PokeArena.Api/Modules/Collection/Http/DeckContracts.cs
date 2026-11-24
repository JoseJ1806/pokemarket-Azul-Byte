using PokeArena.Api.Modules.Collection.Application;
using PokeArena.Api.Modules.Collection.Domain;

namespace PokeArena.Api.Modules.Collection.Http;

// CUERPO de POST /api/me/decks. Solo trae el nombre y las cartas: el jugador sale del
// token, nunca del cuerpo.
public sealed record CreateDeckRequest(string? Name, List<int>? CardIds);

// Un deck tal como lo recibe el cliente en la lista y al crearlo.
public sealed record DeckResponse(
    Guid Id,
    string Name,
    int CardCount,
    IReadOnlyList<int> CardIds,
    DateTimeOffset CreatedAt)
{
    public static DeckResponse From(Deck deck) =>
        new(
            deck.Id,
            deck.Name,
            deck.Cards.Count,
            deck.Cards.Select(card => card.CardId).Order().ToList(),
            deck.CreatedAt);
}

// Una carta dentro del detalle de un deck.
public sealed record DeckCardResponse(
    int CardId,
    string Code,
    string Name,
    string Type,
    string Rarity,
    string? ImageUrl,
    string? ThumbnailUrl);

// RESPUESTA de GET /api/me/decks/{deckId}: el deck con sus cartas ya presentables.
public sealed record DeckDetailResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DeckCardResponse> Cards)
{
    public static DeckDetailResponse From(DeckView view) =>
        new(
            view.Deck.Id,
            view.Deck.Name,
            view.Deck.CreatedAt,
            view.Cards
                .Select(card => new DeckCardResponse(
                    card.Id, card.Code, card.Name, card.Type, card.Rarity, card.ImageUrl, card.ThumbnailUrl))
                .ToList());
}
