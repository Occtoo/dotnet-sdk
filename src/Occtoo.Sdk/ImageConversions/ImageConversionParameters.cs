using CSharpFunctionalExtensions;

namespace Occtoo.ImageConversions;

/// <summary>
/// How to convert an uploaded original. Occtoo checks the values. A value it
/// refuses fails <see cref="ImageConversionsClient.Convert"/> with a
/// <see cref="ValidationError"/> whose <see cref="ValidationError.Failures"/>
/// are keyed <c>Parameters.TargetSize</c>, <c>Parameters.PaddingPercent</c>,
/// <c>Parameters.Quality</c>, <c>Parameters.Mode</c> or
/// <c>Parameters.OutputFormat</c>.
/// </summary>
/// <param name="Mode">How the image is placed on the canvas.</param>
/// <param name="TargetSize">
/// The canvas's longer side, in pixels, from 1 to 8000. The shorter side
/// follows <paramref name="AspectRatio"/>.
/// </param>
/// <param name="AspectRatio">The shape of the canvas.</param>
/// <param name="OutputFormat">The format of the converted image.</param>
/// <param name="Quality">The encoding quality, from 1 to 100.</param>
public sealed record ImageConversionParameters(
    ImageConversionMode Mode,
    int TargetSize,
    ImageAspectRatio AspectRatio,
    ImageOutputFormat OutputFormat,
    int Quality)
{
    /// <summary>
    /// The padding on each side, as a percentage of <see cref="TargetSize"/>,
    /// from 0 to 49. Required with <see cref="ImageConversionMode.FillBackground"/>
    /// and not allowed with any other mode.
    /// </summary>
    public Maybe<int> PaddingPercent { get; init; } = Maybe<int>.None;

    /// <summary>
    /// The color behind transparent areas and padding. Assign
    /// <c>ImageBackground.From("FFFFFF")</c>.
    /// </summary>
    public Maybe<ImageBackground> Background { get; init; } = Maybe<ImageBackground>.None;
}

/// <summary>
/// How the image is placed on the canvas. An original smaller than the canvas
/// is scaled up in every mode.
/// </summary>
public enum ImageConversionMode
{
    /// <summary>
    /// Scales the image to fit inside the canvas, keeping its proportions. One
    /// side can come out shorter than the canvas.
    /// </summary>
    Fit,

    /// <summary>
    /// Scales the image to fit inside the canvas less the padding, then fills
    /// the rest of the canvas with the background. The result is exactly the
    /// canvas size. Requires <see cref="ImageConversionParameters.PaddingPercent"/>.
    /// </summary>
    FillBackground,

    /// <summary>
    /// Scales the image to cover the whole canvas and crops what overflows,
    /// keeping the center.
    /// </summary>
    Crop,

    /// <summary>
    /// Crops when the image and the canvas have the same orientation, and fits
    /// otherwise.
    /// </summary>
    Auto,
}

/// <summary>The format of the converted image.</summary>
public enum ImageOutputFormat
{
    /// <summary>JPEG.</summary>
    Jpg,

    /// <summary>PNG.</summary>
    Png,

    /// <summary>WebP.</summary>
    Webp,
}
