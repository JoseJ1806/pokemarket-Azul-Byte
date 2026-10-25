using PokeArena.Api.Modules.Catalog.Domain;
using PokeArena.Api.Modules.Locations.Domain.Effects;

namespace PokeArena.Api.Modules.Locations.Infrastructure;

// ADAPTADOR de ICardTypeLookup. Es el único punto donde el módulo Ubicaciones lee la lista
// de tipos del módulo Catálogo, para no acoplar el dominio de las ubicaciones con las cartas.
public sealed class CatalogCardTypeLookup : ICardTypeLookup
{
    public bool Exists(string cardType) => CardRules.TypePrefixes.ContainsKey(cardType);
}