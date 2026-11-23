using PokeArena.Api.Modules.Images.Application;

namespace PokeArena.Api.Modules.Images.Infrastructure;

// ADAPTADOR AlmacenImagenes.
// Guarda las imágenes dentro de wwwroot para que los archivos estáticos las sirvan.
// Cambiar a otro almacenamiento solo requiere otra implementación de IImageStore.
public sealed class FileSystemImageStore(IWebHostEnvironment environment) : IImageStore
{
    private string RootPath => Path.GetFullPath(
        environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));

    public async Task SaveAsync(string relativePath, byte[] content, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public string GetUrl(string relativePath) => "/" + relativePath.Replace('\\', '/').TrimStart('/');

    // Evita que una ruta relativa salga de la carpeta wwwroot.
    private string Resolve(string relativePath)
    {
        var root = RootPath;
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La ruta de la imagen queda fuera de wwwroot.");

        return fullPath;
    }
}
