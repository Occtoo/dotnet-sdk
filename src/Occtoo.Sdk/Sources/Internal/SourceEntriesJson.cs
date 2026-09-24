using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Http.Internal;

namespace Occtoo.Sources.Internal;

// Wire mirrors of the stored-entry read contract. Values arrive as the JSON
// type the configured property type dictates and map onto the same
// PropertyValue union ingest writes.

internal sealed record StoredEntryDto
{
    public required string Id { get; init; }

    public required StoredPropertyDto[] Properties { get; init; }

    public DateTimeOffset LastUpdated { get; init; }

    internal StoredSourceEntry ToModel() => new(
        EntryId.From(Id),
        [.. OcctooTransport.Elements(Properties, "properties").Select(property => property.ToModel())],
        LastUpdated);
}

internal sealed record StoredPropertyDto
{
    public required string Id { get; init; }

    public JsonElement Value { get; init; }

    public string? Type { get; init; }

    public string? Delimiter { get; init; }

    public DateTimeOffset LastUpdated { get; init; }

    public string? Language { get; init; }

    internal StoredProperty ToModel()
    {
        var type = Enums.Read<SourcePropertyType>(Type);
        return new(
            PropertyId.From(Id),
            ReadValue(Value, type),
            type,
            Sources.Delimiter.Read(Delimiter),
            LastUpdated,
            Language is { Length: > 0 } && LanguageCode.TryFrom(Language, out var language)
                ? Maybe.From(language)
                : Maybe<LanguageCode>.None);
    }

    // Values never fall back silently: anything the PropertyValue union cannot
    // hold faithfully is a DataTypeError naming the property.
    private PropertyValue ReadValue(JsonElement value, Maybe<SourcePropertyType> type) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => PropertyValue.Clear,
        JsonValueKind.True => PropertyValue.Boolean(true),
        JsonValueKind.False => PropertyValue.Boolean(false),
        JsonValueKind.Array => PropertyValue.List([.. value.EnumerateArray().Select(ListItem)]),
        JsonValueKind.Number when type == SourcePropertyType.Integer && value.TryGetInt64(out var integer) =>
            PropertyValue.Integer(integer),
        JsonValueKind.Number when value.TryGetDecimal(out var number) => PropertyValue.Decimal(number),
        JsonValueKind.Number => throw new DataTypeException(
            $"Property '{Id}' holds {value.GetRawText()}, outside the range a decimal can represent."),
        JsonValueKind.String when type == SourcePropertyType.Timestamp
                                  && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                                      DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) =>
            PropertyValue.Timestamp(timestamp),
        JsonValueKind.String => PropertyValue.Text(value.GetString()!),
        _ => throw new DataTypeException($"Property '{Id}' holds a JSON {value.ValueKind}, which no property type represents."),
    };

    private string ListItem(JsonElement item) =>
        item.ValueKind == JsonValueKind.String
            ? item.GetString()!
            : throw new DataTypeException($"Property '{Id}' holds a list item that is a JSON {item.ValueKind}, not a string.");
}

internal sealed record StoredEntriesDto
{
    public required StoredEntryDto[] Items { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(StoredEntryDto))]
[JsonSerializable(typeof(StoredEntriesDto))]
internal sealed partial class SourceEntriesJsonContext : JsonSerializerContext;
