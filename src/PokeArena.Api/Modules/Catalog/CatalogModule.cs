using PokeArena.Api.Modules.Catalog.Application;
using PokeArena.Api.Modules.Catalog.Http;
using PokeArena.Api.Modules.Catalog.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;

namespace PokeArena.Api.Modules.Catalog;

// Registro del módulo Catálogo de Cartas: une el puerto con su adaptador,
// registra los casos de uso y publica sus endpoints.
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<ICardRepository, EfCardRepository>();
        // Interfaz pública de consulta: la usan otros módulos (hoy, Colección).
        services.AddScoped<ICardCatalogQuery, EfCardCatalogQuery>();
        services.AddScoped<CreateCardUseCase>();
        services.AddScoped<GetCardUseCase>();
        services.AddScoped<GetCatalogQueryHandler>();
        services.AddScoped<ICatalogService>(serviceProvider =>
            new CachedCatalogService(
                serviceProvider.GetRequiredService<GetCatalogQueryHandler>(),
                serviceProvider.GetRequiredService<IDistributedCache>(),
                serviceProvider.GetRequiredService<ILogger<CachedCatalogService>>()));
        return services;
    }

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCreateCard();
        app.MapGetCard();
        app.MapExploreCatalog();
        return app;
    }
}
