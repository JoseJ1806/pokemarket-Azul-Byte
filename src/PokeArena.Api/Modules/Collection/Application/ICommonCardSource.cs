namespace PokeArena.Api.Modules.Collection.Application;

// PUERTO: ORIGEN DE LAS CARTAS COMUNES.
//
// Qué es: la pregunta que Colección le hace al catálogo: "¿qué cartas Comunes puedo
// entregar?". Solo pide identificadores, no cartas completas: con unas 10.000 cartas en
// el catálogo, cargar todas las entidades sería un desperdicio.
//
// Por qué es un puerto: el módulo de Colección no lee las tablas de otro módulo
// (driver D-04). Quien sabe qué significa "Común" y qué es "disponible" es el Catálogo
// (decisión 3 del diseño); aquí solo se pide el resultado.
public interface ICommonCardSource
{
    // Identificadores de las cartas Comunes que están DISPONIBLES (imagen lista).
    // Las que no están disponibles no cuentan para las 15 (decisión H).
    Task<IReadOnlyList<int>> GetAvailableCommonCardIdsAsync(CancellationToken cancellationToken);
}
