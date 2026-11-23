using PokeArena.Api.Modules.Matches.Application;
using PokeArena.Api.Modules.Matches.Http;
using PokeArena.Api.Modules.Matches.Infrastructure;
using PokeArena.GameEngine;

namespace PokeArena.Api.Modules.Matches;

public static class MatchesModule
{
    public static IServiceCollection AddMatchesModule(this IServiceCollection services)
    {
        services.AddScoped<IMatchRepository, EfMatchRepository>();
        services.AddScoped<StartMatchUseCase>();
        services.AddScoped<IGameEngine, InitialMatchGameEngine>();
        return services;
    }

    public static IEndpointRouteBuilder MapMatchesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMatchEndpoints();
        return app;
    }
}
