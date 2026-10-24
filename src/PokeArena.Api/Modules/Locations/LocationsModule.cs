using PokeArena.Api.Modules.Locations.Application;
using PokeArena.Api.Modules.Locations.Domain.Effects;
using PokeArena.Api.Modules.Locations.Infrastructure;

namespace PokeArena.Api.Modules.Locations;

// Registro del módulo Ubicaciones. Más adelante se agregan el repositorio, el caso de uso y los endpoints.
public static class LocationsModule
{
    public static IServiceCollection AddLocationsModule(this IServiceCollection services)
    {
        services.AddSingleton<ICardTypeLookup, CatalogCardTypeLookup>();
        services.AddSingleton<ILocationEffectType, TypeBonusEffect>();
        services.AddSingleton<ILocationEffectType, CostModifierEffect>();
        services.AddSingleton<ILocationEffectType, TurnRestrictionEffect>();
        services.AddSingleton<LocationEffectCatalog>();
        return services;
    }
}