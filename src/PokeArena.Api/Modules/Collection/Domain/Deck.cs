namespace PokeArena.Api.Modules.Collection.Domain;

// DECK de un jugador (módulo Colección, capa de dominio).
//
// Qué es: una lista con nombre de exactamente 12 cartas distintas que el jugador eligió de
// su colección para usarlas en sus partidas. Un jugador puede tener varios decks.
//
// Sin llaves foráneas hacia otros módulos (misma decisión G de la colección): PlayerId y
// CardId son solo Guid y números. La única relación que la base verifica es la de
// DeckCard con su propio Deck, que sí es interna del módulo.
public sealed class Deck
{
    // El Guid lo asigna la aplicación al crear el deck, no la base de datos.
    public Guid Id { get; private set; }

    // Dueño del deck: PlayerAccount.Id (módulo Identidad). Siempre sale del token.
    public Guid PlayerId { get; private set; }

    // Nombre sin espacios sobrantes. No se repite entre los decks del mismo jugador.
    public string Name { get; private set; } = string.Empty;

    // Llave de idempotencia con la que se creó. Un reintento de red con la misma llave
    // devuelve este mismo deck en lugar de crear otro.
    public string IdempotencyKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    // Las 12 cartas del deck.
    public List<DeckCard> Cards { get; private set; } = [];

    // Constructor vacío y privado: lo necesita Entity Framework para leer de la base.
    private Deck()
    {
    }

    // MÉTODO DE FÁBRICA. Quien lo llame ya validó el nombre y las 12 cartas (lo hace el
    // caso de uso); aquí solo se arma el objeto.
    public static Deck Create(
        Guid playerId,
        string name,
        string idempotencyKey,
        IReadOnlyCollection<int> cardIds,
        DateTimeOffset createdAt)
    {
        var id = Guid.NewGuid();
        return new Deck
        {
            Id = id,
            PlayerId = playerId,
            Name = name,
            IdempotencyKey = idempotencyKey,
            CreatedAt = createdAt,
            Cards = cardIds.Select(cardId => DeckCard.Create(id, cardId)).ToList(),
        };
    }
}

// UNA CARTA DENTRO DE UN DECK.
//
// Su clave primaria es (DeckId, CardId): la base de datos es incapaz de guardar la misma
// carta dos veces en el mismo deck, aunque el código de la aplicación se equivocara.
public sealed class DeckCard
{
    public Guid DeckId { get; private set; }

    public int CardId { get; private set; }

    // Constructor vacío y privado para Entity Framework (igual que CardCopy).
    private DeckCard()
    {
    }

    // MÉTODO DE FÁBRICA de una carta dentro de un deck.
    public static DeckCard Create(Guid deckId, int cardId) =>
        new()
        {
            DeckId = deckId,
            CardId = cardId,
        };
}
