namespace PokeArena.Api.Modules.Collection.Application;

// PUERTO: FUENTE DE AZAR.
//
// Qué es: una interfaz mínima que dice "dame un número al azar". Solo eso.
//
// Por qué existe: la lógica de selección necesita azar, pero si usara directamente
// un generador real, nunca podríamos predecir su resultado en una prueba. Con este
// puerto, la aplicación usa SystemRandomSource y las pruebas usan un doble que responde
// lo que queramos. (RNF-ARQ-01: el dominio no depende de la aleatoriedad.)
//
// Por qué es tan pequeño: el ALGORITMO de selección vive en nuestro código
// (InitialCardSelector) y se prueba entero. Este puerto solo aporta números.
public interface IRandomSource
{
    // Devuelve un entero n tal que 0 <= n < maxExclusive.
    int Next(int maxExclusive);
}