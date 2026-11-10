using System.Security.Cryptography;
using PokeArena.Api.Modules.Collection.Application;

namespace PokeArena.Api.Modules.Collection.Infrastructure;

// IMPLEMENTACIÓN REAL del puerto de azar, para producción.
//
// Usa el generador criptográfico del sistema (RandomNumberGenerator) y no Random: es igual
// de simple, reparte los números de forma pareja (sin sesgo) y no se puede predecir. Eso
// importa porque las cartas tienen valor en el juego (RNF-SEG-06: el azar lo decide el
// servidor y el cliente no puede influirlo).
public sealed class SystemRandomSource : IRandomSource
{
    // GetInt32(n) devuelve un número en [0, n): justo lo que pide el puerto.
    public int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}