using SkiaSharp;

namespace PokeArena.Api.Services;

public sealed record CardImageResult(bool IsValid, string? Error, string Extension);

public sealed class CardImageService
{
    public const long MaxFileSize = 5 * 1024 * 1024;
    private const double RequiredRatio = 5d / 7d;
    private const int MinimumWidth = 600;
    private const int MinimumHeight = 840;

    private static readonly IReadOnlyDictionary<string, string> AllowedTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/webp"] = ".webp"
        };

    public async Task<CardImageResult> ValidateAndStoreAsync(
        IFormFile file,
        string originalPath,
        string thumbnailPath,
        CancellationToken cancellationToken)
    {
        if (!AllowedTypes.TryGetValue(file.ContentType, out var extension))
            return new(false, "image debe ser PNG, JPG o WebP.", string.Empty);

        if (file.Length <= 0 || file.Length > MaxFileSize)
            return new(false, "image no puede superar 5 MB y debe tener contenido.", string.Empty);

        await using var input = new MemoryStream();
        await file.CopyToAsync(input, cancellationToken);
        input.Position = 0;

        try
        {
            using var encodedInput = SKData.CreateCopy(input.ToArray());
            using var image = SKBitmap.Decode(encodedInput);
            if (image is null)
                return new(false, "image no contiene un formato de imagen válido.", string.Empty);

            var ratio = (double)image.Width / image.Height;
            if (Math.Abs(ratio - RequiredRatio) > 0.01)
                return new(false, "image debe tener proporción vertical 5:7.", string.Empty);

            if (image.Width < MinimumWidth || image.Height < MinimumHeight)
                return new(false, "image debe tener resolución mínima de 600 x 840 píxeles.", string.Empty);

            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);

            input.Position = 0;
            await using (var original = File.Create(originalPath))
                await input.CopyToAsync(original, cancellationToken);

            using var thumbnail = image.Resize(new SKImageInfo(200, 280), SKSamplingOptions.Default);
            using var thumbnailImage = SKImage.FromBitmap(thumbnail);
            using var encodedThumbnail = thumbnailImage.Encode(SKEncodedImageFormat.Webp, 85);
            await File.WriteAllBytesAsync(thumbnailPath, encodedThumbnail.ToArray(), cancellationToken);
            return new(true, null, extension);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new(false, "image no contiene un formato de imagen válido.", string.Empty);
        }
    }
}
