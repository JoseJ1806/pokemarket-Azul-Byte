using Microsoft.Extensions.Logging;
using PokeArena.Api.Shared;

namespace PokeArena.Api.Modules.Images.Application;

// Imagen ya validada. Thumbnail es null cuando la versión pequeña falló en todos los intentos.
public sealed record PreparedImage(string Extension, byte[] Original, byte[]? Thumbnail);

// Rutas y URLs de los archivos guardados. ThumbnailPath es null si no hay versión pequeña.
public sealed record StoredImage(string OriginalPath, string OriginalUrl, string? ThumbnailPath, string? ThumbnailUrl);

public sealed record PrepareImageResult(PreparedImage? Image, DomainError? Error)
{
    public static PrepareImageResult Valid(PreparedImage image) => new(image, null);

    public static PrepareImageResult Invalid(string message) =>
        new(null, DomainError.Validation("invalid-image", message, "image"));
}

// CASO DE USO: PROCESAR IMAGEN (módulo Imágenes).
// 1. Prepare valida la imagen y genera la versión pequeña, sin tocar el disco.
// 2. StoreAsync guarda ambas versiones.
// 3. DiscardAsync borra los archivos si luego falla el guardado en la base de datos.
// La generación es síncrona dentro de la solicitud, con hasta 3 intentos (ADR-002).
public sealed class ProcessImageUseCase(IImageCodec codec, IImageStore store, ILogger<ProcessImageUseCase> logger)
{
    public const int MaxThumbnailAttempts = 3;

    // Diferencia máxima tolerada entre la proporción pedida y la de la imagen.
    private const double RatioTolerance = 0.01;

    private static readonly IReadOnlyDictionary<string, string> AllowedTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/webp"] = ".webp"
        };

    public PrepareImageResult Prepare(
        ImageUpload upload,
        ImageRequirements requirements,
        CancellationToken cancellationToken)
    {
        if (!AllowedTypes.TryGetValue(upload.ContentType ?? string.Empty, out var extension))
            return PrepareImageResult.Invalid("image debe ser PNG, JPG o WebP.");

        if (upload.Length <= 0 || upload.Length > ImageRequirements.MaxFileSize)
            return PrepareImageResult.Invalid("image no puede superar 5 MB y debe tener contenido.");

        var dimensions = codec.Inspect(upload.Content);
        if (dimensions is null)
            return PrepareImageResult.Invalid("image no contiene un formato de imagen válido.");

        var ratio = (double)dimensions.Width / dimensions.Height;
        if (Math.Abs(ratio - requirements.Ratio) > RatioTolerance)
            return PrepareImageResult.Invalid($"image debe tener proporción {requirements.ProportionLabel}.");

        if (dimensions.Width < requirements.MinWidth || dimensions.Height < requirements.MinHeight)
            return PrepareImageResult.Invalid(
                $"image debe tener resolución mínima de {requirements.MinWidth} x {requirements.MinHeight} píxeles.");

        byte[]? thumbnail = null;
        for (var attempt = 1; attempt <= MaxThumbnailAttempts && thumbnail is null; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var created = codec.CreateThumbnail(
                    upload.Content, requirements.ThumbnailWidth, requirements.ThumbnailHeight);
                if (created is { Length: > 0 })
                    thumbnail = created;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Falló el intento {Attempt} de {MaxAttempts} al generar la versión pequeña.",
                    attempt,
                    MaxThumbnailAttempts);
            }
        }

        return PrepareImageResult.Valid(new PreparedImage(extension, upload.Content, thumbnail));
    }

    public async Task<StoredImage> StoreAsync(
        string folder,
        string baseName,
        PreparedImage image,
        CancellationToken cancellationToken)
    {
        var originalPath = $"{folder}/{baseName}-large{image.Extension}";
        await store.SaveAsync(originalPath, image.Original, cancellationToken);

        if (image.Thumbnail is null)
            return new StoredImage(originalPath, store.GetUrl(originalPath), null, null);

        var thumbnailPath = $"{folder}/{baseName}-small.webp";
        try
        {
            await store.SaveAsync(thumbnailPath, image.Thumbnail, cancellationToken);
        }
        catch
        {
            await store.DeleteAsync(originalPath, CancellationToken.None);
            throw;
        }

        return new StoredImage(
            originalPath,
            store.GetUrl(originalPath),
            thumbnailPath,
            store.GetUrl(thumbnailPath));
    }

    public async Task DiscardAsync(StoredImage stored, CancellationToken cancellationToken)
    {
        foreach (var path in new[] { stored.OriginalPath, stored.ThumbnailPath })
        {
            if (path is null)
                continue;

            try
            {
                await store.DeleteAsync(path, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "No se pudo borrar el archivo {Path}.", path);
            }
        }
    }
}
