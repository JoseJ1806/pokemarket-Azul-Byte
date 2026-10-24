using PokeArena.Api.Modules.Catalog.Application;
using PokeArena.Api.Modules.Catalog.Http;
using PokeArena.Api.Modules.Catalog.Infrastructure;

namespace PokeArena.Api.Modules.Catalog;

// Registro del módulo Catálogo de Cartas: une el puerto con su adaptador,
// registra los casos de uso y publica sus endpoints.
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<ICardRepository, EfCardRepository>();
        services.AddScoped<CreateCardUseCase>();
        return services;
    }

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCreateCard();
        return app;
    }
}
