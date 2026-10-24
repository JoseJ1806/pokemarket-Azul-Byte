namespace PokeArena.Api.Modules.Images.Application;

// REQUISITOS DE UNA IMAGEN.
// El módulo de imágenes sirve a cartas y ubicaciones y se parametriza por
// proporción, resolución mínima y tamaño de la versión pequeña (ADR-002).
public sealed record ImageRequirements(
    string ProportionLabel,
    double Ratio,
    int MinWidth,
    int MinHeight,
    int ThumbnailWidth,
    int ThumbnailHeight)
{
    // Peso máximo permitido para cualquier imagen subida: 5 MB.
    public const long MaxFileSize = 5L * 1024 * 1024;

    // Carta: vertical 5:7, mínimo 600 x 840 y versión pequeña de 200 x 280.
    public static readonly ImageRequirements ForCard = new("vertical 5:7", 5d / 7d, 600, 840, 200, 280);

    // Ubicación: horizontal 4:3, mínimo 800 x 600 y versión pequeña de 320 x 240.
    public static readonly ImageRequirements ForLocation = new("horizontal 4:3", 4d / 3d, 800, 600, 320, 240);
}
