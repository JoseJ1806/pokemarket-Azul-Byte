namespace PokeArena.Api.Modules.Collection.Domain;

// COPIA DE UNA CARTA que posee un jugador (módulo Colección, capa de dominio).
//
// Qué es: UNA fila por cada copia (decisión D del diseño). Si un jugador tiene tres
// copias de la misma carta, hay tres filas. Así cada copia puede tener su origen y su
// fecha, y las recompensas futuras son un simple INSERT, sin actualizar contadores.
//
// Sin llaves foráneas (decisiones G y 5): PlayerId y CardId son solo números/Guids. La
// base de datos NO verifica que existan en player_accounts ni en cards, para que los
// módulos Identidad y Catálogo no queden atados a Colección. Ver ADR-004.
public sealed class CardCopy
{
    public int Id { get; private set; }

    // De quién es la copia: PlayerAccount.Id (módulo Identidad).
    public Guid PlayerId { get; private set; }

    // Qué carta es: Card.Id (módulo Catálogo).
    public int CardId { get; private set; }

    // Cómo llegó a la colección.
    public CopyOrigin Origin { get; private set; }

    // Cuándo llegó (UTC).
    public DateTimeOffset AcquiredAt { get; private set; }

    // Constructor vacío y privado: lo necesita Entity Framework para leer de la base.
    private CardCopy()
    {
    }

    // MÉTODO DE FÁBRICA de una copia de la entrega inicial.
    public static CardCopy CreateInitial(Guid playerId, int cardId, DateTimeOffset acquiredAt) =>
        new()
        {
            PlayerId = playerId,
            CardId = cardId,
            Origin = CopyOrigin.Initial,
            AcquiredAt = acquiredAt,
        };
}
