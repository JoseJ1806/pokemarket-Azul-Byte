namespace PokeArena.Api.Modules.Images.Application;

// PUERTOS DEL MÓDULO IMÁGENES.
// El caso de uso solo conoce estas interfaces. Los adaptadores de
// Modules/Images/Infrastructure las implementan con SkiaSharp y el sistema de archivos.

public sealed record ImageDimensions(int Width, int Height);

// Imagen recibida del cliente. Content queda vacío cuando el archivo ya excede el peso máximo.
public sealed record ImageUpload(string ContentType, long Length, byte[] Content);

public interface IImageCodec
{
    // Devuelve null cuando los bytes no son una imagen decodificable.
    ImageDimensions? Inspect(byte[] content);

    // Genera la versión pequeña en WebP. Puede lanzar una excepción si falla.
    byte[] CreateThumbnail(byte[] content, int width, int height);
}

// Puerto AlmacenImagenes del diagrama de arquitectura lógica.
public interface IImageStore
{
    Task SaveAsync(string relativePath, byte[] content, CancellationToken cancellationToken);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);

    string GetUrl(string relativePath);
}
