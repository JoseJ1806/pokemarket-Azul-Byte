namespace PokeArena.Api.Modules.Collection.Domain;

// REGLAS de la colección inicial, en UN solo lugar. Así, si el profe pide cambiar
// "15 cartas" o "1.000 cubos", se cambia aquí y todo el sistema lo toma.
public static class InitialCollectionRules
{
    // Cantidad de cartas Comunes DISTINTAS que recibe un jugador nuevo.
    // Con 15 se puede armar al menos un mazo válido de 12.
    public const int InitialCardCount = 15;

    // Cubos que recibe junto con las cartas. Quedan como el primer movimiento de su historial.
    public const int InitialCubes = 1000;
}