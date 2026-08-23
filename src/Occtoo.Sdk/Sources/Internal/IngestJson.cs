using System.Text.Json;
using System.Text.Json.Serialization;

namespace Occtoo.Sources.Internal;

/// <summary>
/// The body of <c>POST /v1/sources/{sourceId}</c>, wrapping the caller's entries.
/// </summary>
/// <remarks>
/// The record and its collection serialize through the source-generated context;
/// each element goes through <see cref="SourceEntryJsonConverter"/>. Measured in
/// <c>bench/</c> against the streaming path <see cref="System.Net.Http.Json.JsonContent"/>
/// actually uses: writing the model directly is ~2× the throughput and one
/// tenth the allocations of mapping to a mirror DTO tree first. The per-entry
/// granularity (rather than one converter for the whole body) is deliberate:
/// a converter call is not resumable, so the serializer can only flush between
/// converter calls — this shape keeps the writer's rented buffer at entry size
/// regardless of batch size, where a whole-body converter would hold the entire
/// payload per in-flight request.
/// </remarks>
internal sealed record IngestRequestBody(IReadOnlyCollection<SourceEntry> Entries);

/// <summary>
/// Writes one <see cref="SourceEntry"/> in the endpoint's wire shape —
/// <c>{ "id", "properties": [ { "id", "value", "language"? } ] }</c> — straight
/// from the public model, with values as their native JSON types and
/// <c>language</c> omitted for non-localized properties.
/// </summary>
internal sealed class SourceEntryJsonConverter : JsonConverter<SourceEntry>
{
    public override void Write(Utf8JsonWriter writer, SourceEntry entry, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id"u8, entry.Id.Value);
        writer.WriteStartArray("properties"u8);

        foreach (var property in entry.Properties)
        {
            writer.WriteStartObject();
            writer.WriteString("id"u8, property.Id.Value);
            writer.WritePropertyName("value"u8);
            WritePropertyValue(writer, property.Value);
            if (property.Language.HasValue)
                writer.WriteString("language"u8, property.Language.Value.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes one <see cref="PropertyValue"/> as its native JSON type — the wire
    /// shape the typed ingest endpoint validates against.
    /// </summary>
    internal static void WritePropertyValue(Utf8JsonWriter writer, PropertyValue value)
    {
        switch (value)
        {
            case PropertyValue.TextValue text:
                writer.WriteStringValue(text.Value);
                break;

            case PropertyValue.IntegerValue integer:
                writer.WriteNumberValue(integer.Value);
                break;

            case PropertyValue.DecimalValue number:
                writer.WriteNumberValue(number.Value);
                break;

            case PropertyValue.BooleanValue boolean:
                writer.WriteBooleanValue(boolean.Value);
                break;

            case PropertyValue.TimestampValue timestamp:
                writer.WriteStringValue(timestamp.Value);
                break;

            case PropertyValue.ListValue list:
                writer.WriteStartArray();
                foreach (var item in list.Items)
                    writer.WriteStringValue(item);
                writer.WriteEndArray();
                break;

            case PropertyValue.ClearValue:
                writer.WriteNullValue();
                break;

            default:
                throw new JsonException($"Unknown property value shape {value.GetType()}.");
        }
    }

    public override SourceEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Source entries are only written, never read.");
}

/// <summary>Result body of a <c>202 Accepted</c> typed ingest response.</summary>
/// <remarks>Naming comes from the context's Web defaults (camelCase).</remarks>
internal sealed record TypedIngestAcceptedDto
{
    public Guid CorrelationId { get; init; }

    public string? SourceId { get; init; }

    public DateTimeOffset AcceptedAt { get; init; }

    public int AcceptedEntryCount { get; init; }

    public IReadOnlyList<TypedIngestNewPropertyDto>? NewPropertiesFound { get; init; }
}

internal sealed record TypedIngestNewPropertyDto
{
    public string? Id { get; init; }

    public string? Type { get; init; }

    public string? Delimiter { get; init; }
}

/// <summary>
/// Source-generated serialization for the ingest payloads, so the SDK stays
/// usable under trimming and native AOT.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, Converters = [typeof(SourceEntryJsonConverter)])]
[JsonSerializable(typeof(IngestRequestBody))]
[JsonSerializable(typeof(TypedIngestAcceptedDto))]
internal sealed partial class IngestJsonContext : JsonSerializerContext;
