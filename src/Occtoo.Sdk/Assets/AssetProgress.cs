using CSharpFunctionalExtensions;

namespace Occtoo.Assets;

/// <summary>How far one asset got.</summary>
public enum AssetUploadStage
{
    /// <summary>Occtoo is being asked to create the asset and sign its upload link.</summary>
    Initializing,

    /// <summary>The bytes are moving to blob storage.</summary>
    Transferring,

    /// <summary>Occtoo is being asked to read the blob back and create the file.</summary>
    Completing,

    /// <summary>The file exists.</summary>
    Completed,
}

/// <summary>
/// One report about an asset's progress through <see cref="AssetsClient.Upload"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each asset reports once when it starts initializing, once when its transfer
/// starts, at intervals while the bytes move, once when it starts completing,
/// and once when it is done. A failure ends the reporting for that asset with a
/// single report carrying the stage it reached and the error.
/// </para>
/// <para>
/// Byte-level reports are throttled, and the last one of a transfer always
/// carries the full count.
/// </para>
/// <para>
/// A transfer sent again reads its content from the start, so
/// <see cref="BytesTransferred"/> drops back to zero and counts up again. A
/// progress bar has to follow it down rather than assume it only rises.
/// </para>
/// </remarks>
/// <param name="Key">The asset this is about.</param>
/// <param name="Filename">The name the file is stored under.</param>
/// <param name="Stage">How far it got.</param>
/// <param name="BytesTransferred">How many bytes have left the machine.</param>
/// <param name="TotalBytes">How many there are in total.</param>
/// <param name="Failure">What went wrong, on a report that ends the asset.</param>
public sealed record AssetProgress(
    AssetKey Key,
    AssetFilename Filename,
    AssetUploadStage Stage,
    long BytesTransferred,
    Maybe<long> TotalBytes,
    Maybe<OcctooError> Failure);
