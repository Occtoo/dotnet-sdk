using CSharpFunctionalExtensions;

namespace Occtoo.Assets;

/// <summary>
/// The file Occtoo created, as it read the uploaded blob back.
/// </summary>
/// <remarks>
/// The mime type and the dimensions are the server's reading of the bytes, not
/// anything the caller sent: whatever <c>Content-Type</c> the transfer carried
/// is discarded.
/// </remarks>
/// <param name="Filename">The name the file is stored under.</param>
/// <param name="MimeType">What Occtoo determined the file to be.</param>
/// <param name="Size">The size of the stored file, in bytes.</param>
/// <param name="PublicUrl">Where the file is served from.</param>
/// <param name="Width">The image's width in pixels, for a file Occtoo read as an image.</param>
/// <param name="Height">The image's height in pixels, for a file Occtoo read as an image.</param>
public sealed record AssetFileInfo(
    AssetFilename Filename,
    string MimeType,
    long Size,
    Uri PublicUrl,
    Maybe<int> Width,
    Maybe<int> Height);
