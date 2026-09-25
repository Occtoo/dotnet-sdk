using System.Text.Json;
using System.Text.Json.Serialization;

namespace Occtoo.Assets.Internal;

// The wire shapes of the asset endpoints. Plain DTOs through a source-generated
// context: the payloads are a handful of short strings per asset, so a
// hand-written converter would buy nothing (see docs/conventions.md).

/// <summary>One asset in a request body.</summary>
internal sealed record AssetDto(string EntryKey, string Filename);

/// <summary>The body of <c>POST /v1/assets/{dataSourceId}</c>.</summary>
internal sealed record InitializeAssetsRequestDto(Guid? FolderId, IReadOnlyList<AssetDto> Assets);

/// <summary>The body of the <c>complete</c> and <c>uploadLinks</c> calls.</summary>
internal sealed record AssetsRequestDto(IReadOnlyList<AssetDto> Assets);

internal sealed record InitializedAssetsDto
{
    public Dictionary<string, AssetInitializationDto>? Initialized { get; init; }

    public Dictionary<string, string>? Failed { get; init; }
}

internal sealed record AssetInitializationDto
{
    public string? UploadUrl { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}

internal sealed record CompletedAssetsDto
{
    public Dictionary<string, CompletedAssetDto>? Completed { get; init; }

    public Dictionary<string, string>? Failed { get; init; }
}

internal sealed record CompletedAssetDto
{
    public string? Filename { get; init; }

    public string? MimeType { get; init; }

    public long Size { get; init; }

    public string? PublicUrl { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }
}

/// <summary>
/// One asset's state. <c>Status</c> is read as a string rather than an enum:
/// the platform writes enum names as they are declared, and a status added
/// server-side must not break an SDK that predates it.
/// </summary>
internal sealed record AssetStateDto
{
    public string? Status { get; init; }

    public string? Filename { get; init; }

    public string? MimeType { get; init; }

    public long? Size { get; init; }

    public string? PublicUrl { get; init; }
}

/// <summary>
/// Source-generated serialization for the asset payloads, so the SDK stays
/// usable under trimming and native AOT.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(InitializeAssetsRequestDto))]
[JsonSerializable(typeof(AssetsRequestDto))]
[JsonSerializable(typeof(InitializedAssetsDto))]
[JsonSerializable(typeof(CompletedAssetsDto))]
[JsonSerializable(typeof(Dictionary<string, AssetStateDto>))]
internal sealed partial class AssetsJsonContext : JsonSerializerContext;
