using PokeArena.Api.Modules.Locations.Application;
using PokeArena.Api.Modules.Locations.Domain.Effects;
using PokeArena.Api.Modules.Locations.Http;
using PokeArena.Api.Modules.Locations.Infrastructure;

namespace PokeArena.Api.Modules.Locations;

// Registro del módulo Ubicaciones: une el puerto con su adaptador, registra los
// casos de uso y publica sus endpoints.
public static class LocationsModule
{
    public static IServiceCollection AddLocationsModule(this IServiceCollection services)
    {
        services.AddSingleton<ICardTypeLookup, CatalogCardTypeLookup>();
        services.AddSingleton<ILocationEffectType, TypeBonusEffect>();
        services.AddSingleton<ILocationEffectType, CostModifierEffect>();
        services.AddSingleton<ILocationEffectType, TurnRestrictionEffect>();
        services.AddSingleton<LocationEffectCatalog>();

        services.AddScoped<ILocationRepository, EfLocationRepository>();
        services.AddScoped<CreateLocationUseCase>();
        return services;
    }

    public static IEndpointRouteBuilder MapLocationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCreateLocation();
        return app;
    }
}