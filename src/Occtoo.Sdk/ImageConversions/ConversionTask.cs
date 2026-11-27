using CSharpFunctionalExtensions;

namespace Occtoo.ImageConversions;

/// <summary>Where a queued conversion stands.</summary>
public enum ConversionTaskStatus
{
    /// <summary>
    /// The conversion is waiting to run, either for the first time or again
    /// after Occtoo retries it.
    /// </summary>
    Queued,

    /// <summary>The conversion is running.</summary>
    Running,

    /// <summary>The conversion finished and the converted image is ready to download.</summary>
    Completed,

    /// <summary>The conversion failed and won't be retried.</summary>
    Failed,
}

/// <summary>
/// What Occtoo knows about one queued conversion.
/// </summary>
/// <remarks>
/// Only a <see cref="ConversionTaskStatus.Completed"/> task carries an image,
/// and only a <see cref="ConversionTaskStatus.Failed"/> task carries an error.
/// A status this SDK version doesn't know reads as absent; the image and the
/// error are still filled in when Occtoo sends them.
/// </remarks>
/// <param name="Id">The task's id.</param>
/// <param name="Status">Where the conversion stands.</param>
/// <param name="Image">The converted image, once the conversion completed.</param>
/// <param name="Error">Why the conversion failed, once it failed.</param>
public sealed record ConversionTask(
    ConversionTaskId Id,
    Maybe<ConversionTaskStatus> Status,
    Maybe<ConvertedImage> Image,
    Maybe<string> Error);
