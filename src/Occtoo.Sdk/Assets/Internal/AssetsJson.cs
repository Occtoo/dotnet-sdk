using System.Text.Json;
using System.Text.Json.Serialization;
using Occtoo.Sources;
using Occtoo.Sources.Internal;

namespace Occtoo.Assets.Internal;

// The wire shapes of the asset endpoints. Plain DTOs through a source-generated
// context, except the asset entries in the initialize body: their properties
// scale with user data and PropertyValue has no declarative spelling, so a
// converter writes them (see docs/conventions.md).

/// <summary>One asset in a request body.</summary>
internal sealed record AssetDto(string EntryKey, string Filename);

/// <summary>
/// One asset in the initialize body: what identifies it, and the property
/// values its entry should be created with.
/// </summary>
internal sealed record InitializeAssetDto(AssetDto Asset, IReadOnlyList<EntryProperty> Properties);

/// <summary>
/// The body of <c>POST /v1/assets/{dataSourceId}</c> and
/// <c>PUT /v1/assets/{dataSourceId}</c>.
/// </summary>
internal sealed record InitializeAssetsRequestDto(Guid? FolderId, IReadOnlyList<InitializeAssetDto> Assets);

/// <summary>
/// Writes one asset in the initialize body: <c>entryKey</c>, <c>filename</c>
/// and, when it has any, <c>properties</c> — the same rows typed ingest
/// sends.
/// </summary>
internal sealed class InitializeAssetJsonConverter : JsonConverter<InitializeAssetDto>
{
    public override void Write(Utf8JsonWriter writer, InitializeAssetDto asset, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("entryKey"u8, asset.Asset.EntryKey);
        writer.WriteString("filename"u8, asset.Asset.Filename);

        if (asset.Properties.Count > 0)
        {
            writer.WritePropertyName("properties"u8);
            SourceEntryJsonConverter.WriteProperties(writer, asset.Properties);
        }

        writer.WriteEndObject();
    }

    public override InitializeAssetDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Assets are only written, never read.");
}

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
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(InitializeAssetJsonConverter)])]
[JsonSerializable(typeof(InitializeAssetsRequestDto))]
[JsonSerializable(typeof(AssetsRequestDto))]
[JsonSerializable(typeof(InitializedAssetsDto))]
[JsonSerializable(typeof(CompletedAssetsDto))]
[JsonSerializable(typeof(Dictionary<string, AssetStateDto>))]
internal sealed partial class AssetsJsonContext : JsonSerializerContext;
