using CSharpFunctionalExtensions;

namespace Occtoo.ImageConversions;

/// <summary>
/// A named set of conversion parameters stored in the tenant.
/// </summary>
/// <param name="Id">The preset's id.</param>
/// <param name="DisplayName">The preset's display name.</param>
/// <param name="Parameters">
/// How the preset converts an image. Absent when Occtoo returns a mode or an
/// output format this SDK version doesn't know.
/// </param>
/// <param name="Version">The preset's version. It goes up when the parameters change.</param>
/// <param name="CreatedAt">When the preset was created.</param>
/// <param name="UpdatedAt">When the preset last changed.</param>
public sealed record ConversionPreset(
    ConversionPresetId Id,
    string DisplayName,
    Maybe<ImageConversionParameters> Parameters,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
