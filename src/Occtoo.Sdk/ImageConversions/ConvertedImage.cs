namespace Occtoo.ImageConversions;

/// <summary>
/// One conversion's result: a temporary link to download the converted image.
/// </summary>
/// <param name="Url">Where to download the converted image. The link lasts 1 hour.</param>
/// <param name="ExpiresAt">When <paramref name="Url"/> stops working.</param>
/// <param name="Filename">The converted image's filename.</param>
/// <param name="ContentType">The converted image's media type, such as <c>image/webp</c>.</param>
/// <param name="Size">The converted image's size in bytes.</param>
public sealed record ConvertedImage(
    Uri Url,
    DateTimeOffset ExpiresAt,
    string Filename,
    string ContentType,
    long Size);
