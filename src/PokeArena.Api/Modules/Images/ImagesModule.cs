using PokeArena.Api.Modules.Images.Application;
using PokeArena.Api.Modules.Images.Infrastructure;

namespace PokeArena.Api.Modules.Images;

// Registro del módulo Imágenes: une los puertos con sus adaptadores.
public static class ImagesModule
{
    public static IServiceCollection AddImagesModule(this IServiceCollection services)
    {
        services.AddSingleton<IImageCodec, SkiaImageCodec>();
        services.AddSingleton<IImageStore, FileSystemImageStore>();
        services.AddSingleton<ProcessImageUseCase>();
        return services;
    }
}
