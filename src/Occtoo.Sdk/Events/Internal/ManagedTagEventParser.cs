using System.Text.Json;
using CSharpFunctionalExtensions;
using Occtoo.ManagedTags;

namespace Occtoo.Events.Internal;

internal static partial class EventParser
{
    private static ManagedTagId ManagedTagIdOf(JsonElement data) =>
        Guid.TryParse(GetString(data, "managedTagId"), out var id) && id != Guid.Empty
            ? ManagedTagId.From(id)
            : throw new FormatException("A managed tag event must contain a non-empty managedTagId UUID.");

    private static Maybe<Guid> MaybeGuid(JsonElement data, string name) =>
        Guid.TryParse(GetString(data, name), out var id) && id != Guid.Empty
            ? Maybe.From(id)
            : Maybe<Guid>.None;

    private static Maybe<DateTimeOffset> MaybeDateTimeOffset(JsonElement data, string name) =>
        data.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
        && property.TryGetDateTimeOffset(out var timestamp)
            ? Maybe.From(timestamp)
            : Maybe<DateTimeOffset>.None;

    private static DateTimeOffset DateTimeOffsetOf(JsonElement data, string name) =>
        MaybeDateTimeOffset(data, name).GetValueOrDefault();

    private static bool ValueTruncatedOf(JsonElement data) =>
        data.TryGetProperty("valueTruncated", out var truncated) && truncated.ValueKind == JsonValueKind.True;

    private static double DoubleOf(JsonElement data, string name) =>
        data.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number
        && property.TryGetDouble(out var value) ? value : 0;

    private static Maybe<ManagedTagValueContent> ManagedTagContent(JsonElement data)
    {
        if (!data.TryGetProperty("value", out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Maybe<ManagedTagValueContent>.None;

        return value.ValueKind switch
        {
            JsonValueKind.String => ManagedTagValueContent.Text(value.GetString()!),
            JsonValueKind.Object => new ManagedTagValueContent.LocalizedValue(
                value.EnumerateObject().ToDictionary(property => property.Name,
                    property => property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()!
                        : throw new FormatException($"Managed tag translation '{property.Name}' must be a string."),
                    StringComparer.Ordinal)),
            _ => throw new FormatException("Managed tag value content must be a string or a map of translations."),
        };
    }
}
