using PokeArena.Api.Modules.Collection.Application;
using PokeArena.Api.Modules.Collection.Http;
using PokeArena.Api.Modules.Collection.Infrastructure;
using PokeArena.Api.Modules.Identity.Application;

namespace PokeArena.Api.Modules.Collection;

// Registro del módulo Colección: une cada puerto con su adaptador, registra los casos de
// uso y publica sus endpoints. Mismo estilo que CatalogModule y LocationsModule.
public static class CollectionModule
{
    public static IServiceCollection AddCollectionModule(this IServiceCollection services)
    {
        // El azar y el selector no guardan estado: una sola instancia sirve para todos.
        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddSingleton<InitialCardSelector>();

        // Puertos con su adaptador. Scoped: una instancia por solicitud, igual que el DbContext.
        services.AddScoped<ICollectionRepository, EfCollectionRepository>();
        services.AddScoped<ICommonCardSource, CatalogCommonCardSource>();
        services.AddScoped<ICardSummaryLookup, CatalogCardSummaryLookup>();

        // La entrega inicial se ofrece al módulo Identidad por SU puerto: el login llama a
        // IInitialCollectionGranter y recibe este caso de uso. Reemplaza al adaptador
        // provisional NoOpInitialCollectionGranter.
        services.AddScoped<IInitialCollectionGranter, GrantInitialCollectionUseCase>();

        services.AddScoped<GetCollectionUseCase>();
        return services;
    }

    public static IEndpointRouteBuilder MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGetMyCollection();
        return app;
    }
}
