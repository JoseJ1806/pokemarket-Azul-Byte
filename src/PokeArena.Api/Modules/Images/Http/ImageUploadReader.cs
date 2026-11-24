using PokeArena.Api.Modules.Images.Application;

namespace PokeArena.Api.Modules.Images.Http;

// ADAPTADOR DE ENTRADA HTTP del módulo Imágenes.
// Convierte el archivo multipart en un ImageUpload para cualquier endpoint que reciba
// una imagen (cartas y ubicaciones). La regla del peso vive en ImageRequirements: un
// archivo vacío o de más de 5 MB no se carga a memoria y ProcessImageUseCase lo rechaza.
public static class ImageUploadReader
{
    public static async Task<ImageUpload?> ReadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
            return null;

        var content = ImageRequirements.IsWithinMaxFileSize(file.Length)
            ? await ReadAllBytesAsync(file, cancellationToken)
            : Array.Empty<byte>();

        return new ImageUpload(file.ContentType, file.Length, content);
    }

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
