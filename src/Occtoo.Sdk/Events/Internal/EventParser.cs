using System.Text.Json;
using CSharpFunctionalExtensions;
using Occtoo.Sources;

namespace Occtoo.Events.Internal;

/// <summary>
/// Maps CloudEvents envelopes into the typed event records. Hand-rolled over
/// <see cref="JsonElement"/>: CloudEvents attribute names are all-lowercase
/// (<c>specversion</c>, <c>tenantid</c>) so no naming policy fits, and manual
/// mapping stays lenient — an unknown type or a missing optional field never
/// fails the whole page.
/// </summary>
internal static class EventParser
{
    /// <summary>
    /// Parses one CloudEvents envelope. Returns a failure only when the
    /// envelope itself is unusable; an unknown event type parses as
    /// <see cref="UnknownEvent"/>.
    /// </summary>
    internal static Result<CloudEvent, OcctooError> Parse(JsonElement envelope)
    {
        try
        {
            var type = GetString(envelope, "type");
            var sequence = GetString(envelope, "sequence");

            if (type is null || sequence is null)
                return new UnexpectedError("An event envelope is missing its type or sequence.");

            var data = envelope.TryGetProperty("data", out var found) && found.ValueKind == JsonValueKind.Object
                ? found
                : default;

            var evt = Materialize(type, data) with
            {
                Id = Guid.TryParse(GetString(envelope, "id"), out var id) ? id : Guid.Empty,
                Type = type,
                Sequence = EventSequence.From(sequence),
                Source = GetString(envelope, "source") ?? "",
                Subject = GetString(envelope, "subject") ?? "",
                Time = envelope.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.String
                       && time.TryGetDateTimeOffset(out var parsed)
                    ? Maybe.From(parsed)
                    : Maybe<DateTimeOffset>.None,
                CorrelationIds = data.ValueKind == JsonValueKind.Object
                    ? Strings(data, "correlationIds")
                    : [],
                Actor = data.ValueKind == JsonValueKind.Object
                        && data.TryGetProperty("actor", out var actor)
                        && actor.ValueKind == JsonValueKind.Object
                        && GetString(actor, "id") is { Length: > 0 } actorId
                    ? Maybe.From(actorId)
                    : Maybe<string>.None,
            };

            return evt;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new UnexpectedError($"An event envelope could not be parsed: {exception.Message}");
        }
    }

    // The Materialize switch fills payloads via the records' primary
    // constructors; the caller fills the envelope afterwards with `with`.
    private static CloudEvent Materialize(string type, JsonElement data) => type switch
    {
        "source.created" => new SourceCreated(SourceIdOf(data)),
        "source.updated" => new SourceUpdated(SourceIdOf(data), Strings(data, "changes"), PropertyChanges(data)),
        "source.deleted" => new SourceDeleted(SourceIdOf(data)),
        "source_entry.added" => new SourceEntryAdded(
            SourceIdOf(data),
            EntryKeyOf(data),
            VersionOf(data),
            data.TryGetProperty("properties", out var props) && props.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
                ? Maybe.From(props.Clone())
                : Maybe<JsonElement>.None),
        "source_entry.updated" => new SourceEntryUpdated(
            SourceIdOf(data),
            EntryKeyOf(data),
            VersionOf(data),
            PropertyIds(data, "changedProperties"),
            ValuesTruncatedOf(data),
            Values(data)),
        "source_entry.deleted" => new SourceEntryDeleted(SourceIdOf(data), EntryKeyOf(data), VersionOf(data)),
        "card.updated" => new CardUpdated(CardDefinitionIdOf(data), CardIdOf(data), PropertyIds(data, "changedProperties")),
        "card.segment_membership_changed" => new CardSegmentMembershipChanged(
            CardDefinitionIdOf(data),
            CardIdOf(data),
            SegmentIdOf(data),
            GetString(data, "change") ?? ""),
        "card_definition.created" => new CardDefinitionCreated(CardDefinitionIdOf(data)),
        "card_definition.updated" => new CardDefinitionUpdated(CardDefinitionIdOf(data), Strings(data, "changes")),
        "card_definition.activated" => new CardDefinitionActivated(CardDefinitionIdOf(data)),
        "card_definition.deleted" => new CardDefinitionDeleted(CardDefinitionIdOf(data)),
        "card_definition.restored" => new CardDefinitionRestored(CardDefinitionIdOf(data)),
        "segment.created" => new SegmentCreated(SegmentIdOf(data), SegmentDefinitionIdOf(data), MaybeCardDefinitionId(data)),
        "segment.updated" => new SegmentUpdated(
            SegmentIdOf(data),
            SegmentDefinitionIdOf(data),
            MaybeCardDefinitionId(data),
            Strings(data, "changes")),
        "segment.archived" => new SegmentArchived(SegmentIdOf(data), SegmentDefinitionIdOf(data), MaybeCardDefinitionId(data)),
        "segment.restored" => new SegmentRestored(SegmentIdOf(data), SegmentDefinitionIdOf(data), MaybeCardDefinitionId(data)),
        "segment.deleted" => new SegmentDeleted(SegmentIdOf(data), SegmentDefinitionIdOf(data), MaybeCardDefinitionId(data)),
        "destination.created" => new DestinationCreated(DestinationIdOf(data)),
        "destination.published" => new DestinationPublished(DestinationIdOf(data)),
        "destination.stopped" => new DestinationStopped(DestinationIdOf(data)),
        "destination.deleted" => new DestinationDeleted(DestinationIdOf(data)),
        "destination_entry.updated" => new DestinationEntryUpdated(
            DestinationIdOf(data),
            ApiVersionIdOf(data),
            GetString(data, "apiVersion") ?? "",
            EndpointIdOf(data),
            GetString(data, "endpoint") ?? "",
            GetString(data, "entryId") ?? "",
            VersionOf(data),
            PropertyIds(data, "changedProperties"),
            ValuesTruncatedOf(data),
            Values(data)),
        "destination_entry.deleted" => new DestinationEntryDeleted(
            DestinationIdOf(data),
            ApiVersionIdOf(data),
            GetString(data, "apiVersion") ?? "",
            EndpointIdOf(data),
            GetString(data, "endpoint") ?? "",
            GetString(data, "entryId") ?? "",
            VersionOf(data)),
        "endpoint.created" => new EndpointCreated(DestinationIdOf(data), ApiVersionIdOf(data), GetString(data, "apiVersion") ?? "", EndpointIdOf(data)),
        "endpoint.updated" => new EndpointUpdated(DestinationIdOf(data), ApiVersionIdOf(data), GetString(data, "apiVersion") ?? "", EndpointIdOf(data)),
        "endpoint.deleted" => new EndpointDeleted(DestinationIdOf(data), ApiVersionIdOf(data), GetString(data, "apiVersion") ?? "", EndpointIdOf(data)),
        "user.created" => new UserCreated(UserIdOf(data), MaybeString(data, "email"), MaybeString(data, "firstName"), MaybeString(data, "lastName")),
        "user.updated" => new UserUpdated(UserIdOf(data), MaybeString(data, "email"), MaybeString(data, "firstName"), MaybeString(data, "lastName")),
        "user.deleted" => new UserDeleted(UserIdOf(data), MaybeString(data, "email"), MaybeString(data, "firstName"), MaybeString(data, "lastName")),
        _ => new UnknownEvent(data.ValueKind == JsonValueKind.Object ? data.Clone() : default),
    };

    // Lenient id readers: a missing id materializes as "unknown" rather than
    // failing the whole page — the envelope is still delivered.
    private static SourceId SourceIdOf(JsonElement data) => SourceId.From(GetString(data, "sourceId") ?? "unknown");

    private static EntryId EntryKeyOf(JsonElement data) => EntryId.From(GetString(data, "entryKey") ?? "unknown");

    private static CardId CardIdOf(JsonElement data) => CardId.From(GetString(data, "cardId") ?? "unknown");

    private static SegmentId SegmentIdOf(JsonElement data) => SegmentId.From(GetString(data, "segmentId") ?? "unknown");

    private static SegmentDefinitionId SegmentDefinitionIdOf(JsonElement data) =>
        SegmentDefinitionId.From(GetString(data, "segmentDefinitionId") ?? "unknown");

    private static ApiVersionId ApiVersionIdOf(JsonElement data) =>
        ApiVersionId.From(GetString(data, "apiVersionId") ?? "unknown");

    private static EndpointId EndpointIdOf(JsonElement data) =>
        EndpointId.From(GetString(data, "endpointId") ?? "unknown");

    private static UserId UserIdOf(JsonElement data) => UserId.From(GetString(data, "userId") ?? "unknown");

    private static Maybe<CardDefinitionId> MaybeCardDefinitionId(JsonElement data) =>
        GetString(data, "cardDefinitionId") is { Length: > 0 } id
            ? Maybe.From(CardDefinitionId.From(id))
            : Maybe<CardDefinitionId>.None;

    private static bool ValuesTruncatedOf(JsonElement data) =>
        data.TryGetProperty("valuesTruncated", out var truncated) && truncated.ValueKind == JsonValueKind.True;

    private static CardDefinitionId CardDefinitionIdOf(JsonElement data) =>
        CardDefinitionId.From(GetString(data, "cardDefinitionId") ?? "unknown");

    private static DestinationId DestinationIdOf(JsonElement data) =>
        DestinationId.From(GetString(data, "destinationId") ?? "unknown");

    private static long VersionOf(JsonElement data)
    {
        if (!data.TryGetProperty("version", out var version))
            return 0;

        return version.ValueKind switch
        {
            JsonValueKind.Number when version.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(version.GetString(), out var parsed) => parsed,
            _ => 0,
        };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Maybe<string> MaybeString(JsonElement element, string name) =>
        GetString(element, name) is { Length: > 0 } value ? Maybe.From(value) : Maybe<string>.None;

    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Array
            ? [.. property.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    private static IReadOnlyList<PropertyId> PropertyIds(JsonElement element, string name) =>
        [.. Strings(element, name).Where(value => value.Length > 0).Select(PropertyId.From)];

    private static Maybe<IReadOnlyList<SourcePropertyChange>> PropertyChanges(JsonElement data)
    {
        if (!data.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
            return Maybe<IReadOnlyList<SourcePropertyChange>>.None;

        return Maybe.From<IReadOnlyList<SourcePropertyChange>>(
        [
            .. properties.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && GetString(item, "id") is { Length: > 0 })
                .Select(item => new SourcePropertyChange(
                    PropertyId.From(GetString(item, "id")!),
                    GetString(item, "change") ?? "")),
        ]);
    }

    private static Maybe<IReadOnlyDictionary<string, IReadOnlyList<EventPropertyValue>>> Values(JsonElement data)
    {
        if (!data.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
            return Maybe<IReadOnlyDictionary<string, IReadOnlyList<EventPropertyValue>>>.None;

        var result = new Dictionary<string, IReadOnlyList<EventPropertyValue>>(StringComparer.Ordinal);
        foreach (var property in values.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;

            result[property.Name] =
            [
                .. property.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => new EventPropertyValue(
                        GetString(item, "value") ?? "",
                        LanguageCode.TryFrom(GetString(item, "language") ?? "", out var language)
                            ? Maybe.From(language)
                            : Maybe<LanguageCode>.None)),
            ];
        }

        return Maybe.From<IReadOnlyDictionary<string, IReadOnlyList<EventPropertyValue>>>(result);
    }
}
