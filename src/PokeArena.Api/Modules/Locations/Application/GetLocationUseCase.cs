using PokeArena.Api.Modules.Locations.Domain;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Locations.Application;

// Resultado de consultar una ubicación: la ubicación o un error.
public sealed record GetLocationResult(Location? Location, DomainError? Error)
{
    public static GetLocationResult Found(Location location) => new(location, null);

    public static GetLocationResult Failed(DomainError error) => new(null, error);
}

// CASO DE USO Consultar ubicación por id.
// Devuelve la ubicación con su configuración, su efecto y el estado de la imagen. Las
// ubicaciones con imagen Fallida también se pueden consultar, porque el administrador
// necesita ver ese estado.
public sealed class GetLocationUseCase(ILocationRepository locations)
{
    public async Task<GetLocationResult> ExecuteAsync(int id, CancellationToken cancellationToken)
    {
        var location = await locations.FindByIdAsync(id, cancellationToken);
        return location is null
            ? GetLocationResult.Failed(DomainError.NotFound(
                "location-not-found", $"No existe una ubicación con id {id}."))
            : GetLocationResult.Found(location);
    }
}
