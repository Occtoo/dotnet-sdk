using System.Text.Json;
using System.Text.Json.Serialization;
using Occtoo.Sources;

namespace Occtoo.Json;

/// <summary>
/// The serialization conventions every SDK payload follows, so no DTO needs
/// per-property attributes.
/// </summary>
/// <remarks>
/// <para>
/// Occtoo's API speaks camelCase JSON with string enums; the OAuth endpoints
/// speak snake_case. Each source-generated context in the SDK declares the
/// matching convention once via <see cref="JsonSourceGenerationOptionsAttribute"/> —
/// <see cref="JsonSerializerDefaults.Web"/> for Occtoo payloads,
/// <see cref="JsonKnownNamingPolicy.SnakeCaseLower"/> for OAuth — and property
/// names stay attribute-free.
/// </para>
/// <para>
/// The options here exist for the rare non-generated path and for consumers who
/// serialize SDK types themselves and want to match the wire format.
/// </para>
/// </remarks>
public static class OcctooJsonOptions
{
    /// <summary>
    /// How the Occtoo API's payloads are serialized: camelCase property names,
    /// case-insensitive reading, enums as their string names.
    /// </summary>
    /// <remarks>
    /// Enum converters are registered per type rather than through the
    /// reflection-based <c>JsonStringEnumConverter</c>, which requires runtime
    /// code generation and would break consumers publishing with native AOT.
    /// </remarks>
    public static JsonSerializerOptions Api { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<SourcePropertyType>() },
    };
}
