using System.Text.Json;
using System.Text.Json.Serialization;

namespace Occtoo.ImageConversions.Internal;

internal sealed record RequestImageConversionDto(string Filename);

internal sealed record ImageConversionCreatedDto
{
    public Guid Id { get; init; }

    public string? UploadUrl { get; init; }

    public long? MaxFileSize { get; init; }
}

internal sealed record ConvertImageDto
{
    public ImageConversionParametersDto? Parameters { get; init; }

    public string? Preset { get; init; }
}

internal sealed record ImageConversionParametersDto(
    ImageConversionMode Mode,
    int TargetSize,
    string AspectRatio,
    int? PaddingPercent,
    string? Background,
    ImageOutputFormat OutputFormat,
    int Quality);

internal sealed record ConvertedImageDto
{
    public string? Url { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public string Filename { get; init; } = "";

    public string ContentType { get; init; } = "";

    public long Size { get; init; }
}

internal sealed record CreateConversionPresetDto(
    string Id,
    string DisplayName,
    ImageConversionParametersDto Parameters);

internal sealed record UpdateConversionPresetDto(string DisplayName, ImageConversionParametersDto Parameters);

internal sealed record ConversionPresetDto
{
    public string Id { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public int Version { get; init; }

    public required ConversionPresetParametersDto Parameters { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

internal sealed record ConversionPresetParametersDto
{
    public string? Mode { get; init; }

    public int TargetSize { get; init; }

    public string AspectRatio { get; init; } = "";

    public int? PaddingPercent { get; init; }

    public string? Background { get; init; }

    public string? OutputFormat { get; init; }

    public int Quality { get; init; }
}

internal sealed record ForwardPageDto<T>
{
    public required T[] Items { get; init; }

    public string? After { get; init; }

    public long? TotalCount { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(JsonStringEnumConverter<ImageConversionMode>), typeof(JsonStringEnumConverter<ImageOutputFormat>)])]
[JsonSerializable(typeof(RequestImageConversionDto))]
[JsonSerializable(typeof(ImageConversionCreatedDto))]
[JsonSerializable(typeof(ConvertImageDto))]
[JsonSerializable(typeof(ConvertedImageDto))]
[JsonSerializable(typeof(CreateConversionPresetDto))]
[JsonSerializable(typeof(UpdateConversionPresetDto))]
[JsonSerializable(typeof(ConversionPresetDto))]
[JsonSerializable(typeof(ForwardPageDto<ConversionPresetDto>))]
internal sealed partial class ImageConversionsJsonContext : JsonSerializerContext;
