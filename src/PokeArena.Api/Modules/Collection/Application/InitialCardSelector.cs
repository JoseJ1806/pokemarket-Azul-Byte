using PokeArena.Api.Modules.Collection.Domain;

namespace PokeArena.Api.Modules.Collection.Application;

// SELECTOR de la colección inicial: elige 15 cartas DISTINTAS al azar.
//
// Algoritmo: mezcla parcial de Fisher-Yates. Es como sacar bolas de una urna SIN
// devolverlas: se toma una al azar, se aparta, y se repite con las que quedan.
// Como nunca se vuelve a sacar una ya apartada, es imposible que salga repetida.
public sealed class InitialCardSelector(IRandomSource random)
{
    // availableCardIds: identificadores de las Comunes disponibles (los entrega Catálogo).
    // Devuelve exactamente InitialCollectionRules.InitialCardCount identificadores distintos.
    //
    // Contrato: quien llama debe asegurarse de que haya al menos 15 distintas. Si no hay
    // suficientes, el caso de uso responde NotAvailable ANTES de llamar a este método.
    // Por eso aquí, si faltan, se lanza una excepción: sería un error de programación.
    public IReadOnlyList<int> Select(IReadOnlyList<int> availableCardIds)
    {
        var count = InitialCollectionRules.InitialCardCount;

        // Distinct() asegura "distintas" aunque la entrada traiga repetidos. ToArray() hace
        // una COPIA: el algoritmo intercambia elementos y no debe dañar la lista original.
        var pool = availableCardIds.Distinct().ToArray();

        if (pool.Length < count)
            throw new ArgumentException(
                $"Se necesitan al menos {count} cartas distintas y solo hay {pool.Length}.",
                nameof(availableCardIds));

        for (var i = 0; i < count; i++)
        {
            // Se elige una posición al azar entre i y el final (las que NO se han sacado).
            var j = i + random.Next(pool.Length - i);

            // Se intercambia: la carta elegida pasa a la posición i, que queda "apartada".
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        // Las primeras 'count' posiciones son las cartas elegidas.
        return pool[..count];
    }
}