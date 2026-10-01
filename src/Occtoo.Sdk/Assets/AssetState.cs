using CSharpFunctionalExtensions;

namespace Occtoo.Assets;

/// <summary>Where an asset stands, as Occtoo sees it.</summary>
public enum AssetStatus
{
    /// <summary>
    /// The asset exists and is waiting for its bytes. This covers the whole
    /// window from initializing until completion has been accounted for — the
    /// transfer goes straight to storage, so nothing in between is visible to
    /// Occtoo.
    /// </summary>
    Initialized,

    /// <summary>The file exists and is served from its public URL.</summary>
    Completed,

    /// <summary>Occtoo could not make a file out of the uploaded bytes.</summary>
    Failed,
}

/// <summary>
/// What Occtoo knows about one asset.
/// </summary>
/// <remarks>
/// Everything but the status is filled in once a file exists, so an asset that
/// is still <see cref="AssetStatus.Initialized"/> carries nothing else. The
/// status trails a successful completion by one event hop — read it to recover
/// state you lost, not to confirm an upload you just finished.
/// </remarks>
/// <param name="Status">Where the asset stands.</param>
/// <param name="Filename">The name the file is stored under.</param>
/// <param name="MimeType">What Occtoo determined the file to be.</param>
/// <param name="Size">The size of the stored file, in bytes.</param>
/// <param name="PublicUrl">Where the file is served from.</param>
public sealed record AssetState(
    Maybe<AssetStatus> Status,
    Maybe<AssetFilename> Filename,
    Maybe<string> MimeType,
    Maybe<long> Size,
    Maybe<Uri> PublicUrl);
