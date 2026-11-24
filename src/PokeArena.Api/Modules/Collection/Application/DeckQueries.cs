using PokeArena.Api.Modules.Collection.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Collection.Application;

// Resultado de listar los decks del jugador.
public sealed record ListDecksResult(IReadOnlyList<Deck>? Decks, DomainError? Error);

// Un deck con los datos de presentación de sus cartas (nombre, imagen, tipo...).
public sealed record DeckView(Deck Deck, IReadOnlyList<CardSummary> Cards);

// Resultado de consultar un deck.
public sealed record GetDeckResult(DeckView? Deck, DomainError? Error);

// CASO DE USO: LISTAR MIS DECKS (módulo Colección, capa de aplicación).
//
// Igual que GET /api/me/collection, la ruta NO recibe el Id del jugador: sale del token
// firmado, así que es imposible listar los decks de otra persona.
public sealed class ListDecksUseCase(IDeckRepository decks)
{
    public async Task<ListDecksResult> ExecuteAsync(string? subject, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subject, out var playerId))
            return new ListDecksResult(
                null,
                DomainError.NotFound(
                    GetCollectionUseCase.NotAPlayerCode,
                    "La sesión actual no corresponde a una cuenta de jugador."));

        return new ListDecksResult(await decks.ListAsync(playerId, cancellationToken), null);
    }
}

// CASO DE USO: CONSULTAR UN DECK (módulo Colección, capa de aplicación).
//
// Aunque el cliente conozca el identificador de un deck de OTRO jugador, no puede verlo:
// el repositorio busca por el par (jugador del token, deck). Si no existe o es de otro, la
// respuesta es la misma (404), para no revelar que ese deck existe.
public sealed class GetDeckUseCase(IDeckRepository decks, ICardSummaryLookup cards)
{
    public const string DeckNotFoundCode = "deck-not-found";

    public async Task<GetDeckResult> ExecuteAsync(string? subject, Guid deckId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(subject, out var playerId))
            return new GetDeckResult(
                null,
                DomainError.NotFound(
                    GetCollectionUseCase.NotAPlayerCode,
                    "La sesión actual no corresponde a una cuenta de jugador."));

        var deck = await decks.FindAsync(playerId, deckId, cancellationToken);
        if (deck is null)
            return new GetDeckResult(null, DomainError.NotFound(DeckNotFoundCode, "El deck no existe."));

        // Los datos de presentación los entrega Catálogo, a través del puerto.
        var summaries = await cards.GetByIdsAsync(
            deck.Cards.Select(card => card.CardId).ToList(),
            cancellationToken);

        // Orden estable: por nombre y, a igualdad de nombre, por Id.
        var ordered = summaries
            .OrderBy(summary => summary.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.Id)
            .ToList();

        return new GetDeckResult(new DeckView(deck, ordered), null);
    }
}
