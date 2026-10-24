using PokeArena.Api.Modules.Images.Application;
using SkiaSharp;

namespace PokeArena.Api.Modules.Images.Infrastructure;

// ADAPTADOR DE DECODIFICACIÓN Y REDIMENSIONADO.
// Implementa IImageCodec con SkiaSharp. Es el único lugar que conoce esa biblioteca.
public sealed class SkiaImageCodec : IImageCodec
{
    public ImageDimensions? Inspect(byte[] content)
    {
        try
        {
            using var data = SKData.CreateCopy(content);
            using var bitmap = SKBitmap.Decode(data);
            return bitmap is null ? null : new ImageDimensions(bitmap.Width, bitmap.Height);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public byte[] CreateThumbnail(byte[] content, int width, int height)
    {
        using var data = SKData.CreateCopy(content);
        using var bitmap = SKBitmap.Decode(data)
            ?? throw new InvalidOperationException("No se pudo decodificar la imagen.");
        using var resized = bitmap.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default)
            ?? throw new InvalidOperationException("No se pudo redimensionar la imagen.");
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, 85);
        return encoded.ToArray();
    }
}
