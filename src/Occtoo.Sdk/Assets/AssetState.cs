using CSharpFunctionalExtensions;

namespace Occtoo.Assets;

/// <summary>Where an asset stands, as Occtoo sees it.</summary>
public enum AssetStatus
{
    /// <summary>
    /// The asset exists but has no file yet, from
    /// <see cref="AssetsClient.Initialize"/> or <see cref="AssetsClient.Override"/>
    /// until Occtoo has analysed the file. The transfer goes straight to
    /// storage, so Occtoo doesn't see it, and the analysis can run on for a
    /// while after <see cref="AssetsClient.Complete"/>.
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
/// is still <see cref="AssetStatus.Initialized"/> carries nothing else. Right
/// after <see cref="AssetsClient.Complete"/> an asset can still read
/// <see cref="AssetStatus.Initialized"/> for a while because Occtoo is still
/// analysing the file; poll <see cref="AssetsClient.GetState"/> if you need it
/// to read <see cref="AssetStatus.Completed"/>.
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
