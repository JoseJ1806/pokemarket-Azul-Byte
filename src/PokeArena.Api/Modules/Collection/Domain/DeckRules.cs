namespace PokeArena.Api.Modules.Collection.Domain;

// REGLAS DE UN DECK (módulo Colección, capa de dominio).
//
// Están en un solo lugar para que el caso de uso, las pruebas y la base de datos usen
// los mismos números. Los valores salen de la historia "Crear Deck".
public static class DeckRules
{
    // Un deck tiene EXACTAMENTE 12 cartas distintas de la colección del jugador.
    public const int Size = 12;

    // Largo máximo del nombre. La historia no fija una cifra: es una decisión del equipo
    // para que el nombre quepa bien en la pantalla y en la base de datos.
    public const int MaxNameLength = 50;

    // Largo máximo de la Idempotency-Key. Mismo valor que usa Crear Ubicación.
    public const int MaxIdempotencyKeyLength = 200;
}
