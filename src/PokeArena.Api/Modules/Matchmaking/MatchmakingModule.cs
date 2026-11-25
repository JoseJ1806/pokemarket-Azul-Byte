using PokeArena.Api.Modules.Matchmaking.Application;
using PokeArena.Api.Modules.Matchmaking.Http;
using PokeArena.Api.Modules.Matchmaking.Infrastructure;

namespace PokeArena.Api.Modules.Matchmaking;

// Registro del módulo Emparejamiento (Sprint 3, US #127 y #128): une cada puerto con su
// adaptador, registra los casos de uso y publica sus endpoints. Mismo estilo que
// CollectionModule. Necesita los módulos Colección, Identidad y Partida ya registrados.
public static class MatchmakingModule
{
    public static IServiceCollection AddMatchmakingModule(this IServiceCollection services)
    {
        // Puertos con su adaptador. Scoped: una instancia por solicitud, igual que el DbContext.
        services.AddScoped<IMatchSearchRepository, EfMatchSearchRepository>();
        services.AddScoped<ISearchDeckLookup, CollectionSearchDeckLookup>();
        services.AddScoped<ICubeBalanceLookup, CollectionCubeBalanceLookup>();
        services.AddScoped<IPlayerEloLookup, IdentityPlayerEloLookup>();
        services.AddScoped<IActiveMatchLookup, MatchesActiveMatchLookup>();
        services.AddScoped<IPairedMatchStarter, MatchesPairedMatchStarter>();

        services.AddScoped<MatchmakingPairing>();
        services.AddScoped<SearchMatchUseCase>();
        services.AddScoped<GetMatchSearchUseCase>();
        services.AddScoped<CancelMatchSearchUseCase>();
        return services;
    }

    public static IEndpointRouteBuilder MapMatchmakingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMatchSearchEndpoints();
        return app;
    }
}
