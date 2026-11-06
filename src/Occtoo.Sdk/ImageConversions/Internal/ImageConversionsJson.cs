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

internal sealed record ConvertImageDto(ImageConversionParametersDto Parameters);

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

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(JsonStringEnumConverter<ImageConversionMode>), typeof(JsonStringEnumConverter<ImageOutputFormat>)])]
[JsonSerializable(typeof(RequestImageConversionDto))]
[JsonSerializable(typeof(ImageConversionCreatedDto))]
[JsonSerializable(typeof(ConvertImageDto))]
[JsonSerializable(typeof(ConvertedImageDto))]
internal sealed partial class ImageConversionsJsonContext : JsonSerializerContext;
