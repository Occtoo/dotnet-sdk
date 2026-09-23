using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;

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
        [.. Properties.Select(property => property.ToModel())],
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

    internal StoredProperty ToModel() => new(
        PropertyId.From(Id),
        ReadValue(Value, PropertyTypes.Parse(Type).GetValueOrDefault()),
        PropertyTypes.Parse(Type),
        Sources.Delimiter.Read(Delimiter),
        LastUpdated,
        Language is { Length: > 0 } && LanguageCode.TryFrom(Language, out var language)
            ? Maybe.From(language)
            : Maybe<LanguageCode>.None);

    private static PropertyValue ReadValue(JsonElement value, SourcePropertyType type) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => PropertyValue.Clear,
        JsonValueKind.True => PropertyValue.Boolean(true),
        JsonValueKind.False => PropertyValue.Boolean(false),
        JsonValueKind.Array => PropertyValue.List(
            [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]),
        JsonValueKind.Number when type is SourcePropertyType.Integer && value.TryGetInt64(out var integer) =>
            PropertyValue.Integer(integer),
        JsonValueKind.Number when value.TryGetDecimal(out var number) => PropertyValue.Decimal(number),
        JsonValueKind.Number => PropertyValue.Text(value.GetRawText()),
        JsonValueKind.String when type is SourcePropertyType.Timestamp
                                  && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                                      DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) =>
            PropertyValue.Timestamp(timestamp),
        JsonValueKind.String => PropertyValue.Text(value.GetString()!),
        _ => PropertyValue.Text(value.GetRawText()),
    };
}

internal sealed record StoredEntriesDto
{
    public required StoredEntryDto[] Items { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(StoredEntryDto))]
[JsonSerializable(typeof(StoredEntriesDto))]
internal sealed partial class SourceEntriesJsonContext : JsonSerializerContext;
