using System.Text.Json;
using CSharpFunctionalExtensions;

namespace Occtoo.Events;

/// <summary>
/// A change somewhere in the tenant, delivered as a CloudEvents envelope. The
/// concrete type is the event type: pattern match on it.
/// </summary>
/// <remarks>
/// <para>
/// Every documented event type has a sealed record (30 of them, grouped under
/// nine abstract family bases such as <see cref="SourceEntryEvent"/> for
/// coarse matching), and a type this SDK version does not know arrives as
/// <see cref="UnknownEvent"/> — never silently dropped.
/// </para>
/// <code>
/// switch (evt)
/// {
///     case SourceEntryAdded e:   Sync(e.SourceId, e.EntryKey); break;
///     case SourceEntryEvent e:   Log(e.SourceId);              break; // any source_entry.*
///     case UnknownEvent e:       LogUnknown(e.Type, e.Data);   break;
/// }
/// </code>
/// </remarks>
public abstract record CloudEvent
{
    private protected CloudEvent()
    {
    }

    /// <summary>
    /// Parses one CloudEvent as delivered outside the SDK's own transports —
    /// a webhook body, an Azure Service Bus message, an Azure Storage Queue
    /// message. Every destination delivers the same envelope the pull and SSE
    /// APIs return, so the result is the same typed records.
    /// </summary>
    public static Result<CloudEvent, OcctooError> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            return Internal.EventParser.Parse(document.RootElement);
        }
        catch (JsonException exception)
        {
            return new ValidationError($"The payload is not valid JSON: {exception.Message}");
        }
    }

    /// <inheritdoc cref="Parse(string)"/>
    public static Result<CloudEvent, OcctooError> Parse(ReadOnlyMemory<byte> utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json);
            return Internal.EventParser.Parse(document.RootElement);
        }
        catch (JsonException exception)
        {
            return new ValidationError($"The payload is not valid JSON: {exception.Message}");
        }
    }

    /// <inheritdoc cref="Parse(string)"/>
    public static Result<CloudEvent, OcctooError> Parse(JsonElement envelope) =>
        Internal.EventParser.Parse(envelope);

    /// <summary>The event's deterministic idempotency key.</summary>
    public Guid Id { get; init; }

    /// <summary>The event type, exactly as published — <c>source_entry.added</c>.</summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// The event's position in the tenant stream. Fixed-width and ordered;
    /// persist the last processed one as a checkpoint and resume with
    /// <see cref="EventSequence.AsCursor"/>.
    /// </summary>
    public EventSequence Sequence { get; init; }

    /// <summary>The tenant-relative resource path that produced the event.</summary>
    public string Source { get; init; } = "";

    /// <summary>The entity key within the source resource.</summary>
    public string Subject { get; init; } = "";

    /// <summary>
    /// When the domain change occurred, when the producer supplied it. Never
    /// synthesized from delivery time.
    /// </summary>
    public Maybe<DateTimeOffset> Time { get; init; }

    /// <summary>Ids correlating this event with the operation that caused it.</summary>
    public IReadOnlyList<string> CorrelationIds { get; init; } = [];

    /// <summary>
    /// The user or service that caused the change; none for system-initiated
    /// changes.
    /// </summary>
    public Maybe<string> Actor { get; init; }
}

/// <summary>
/// An event of a type this SDK version does not know — the platform added one.
/// The envelope is fully populated; the payload is available raw.
/// </summary>
/// <param name="Data">The raw <c>data</c> payload. Cloned; safe to hold.</param>
public sealed record UnknownEvent(JsonElement Data) : CloudEvent;

// ── Filter capability markers ──────────────────────────────────────────────
// Implemented by the event records whose type supports filtering on the given
// property, so EventFilter's condition methods only exist where the API
// accepts them. Empty on purpose: they describe filterability, not payload
// shape (a segment event is filterable by cardDefinitionId even though its
// payload's cardDefinitionId is optional).

/// <summary>The event type accepts a <c>sourceId</c> filter.</summary>
public interface IFilterableBySource;

/// <summary>The event type accepts a <c>cardDefinitionId</c> filter.</summary>
public interface IFilterableByCardDefinition;

/// <summary>The event type accepts a <c>segmentId</c> filter.</summary>
public interface IFilterableBySegment;

/// <summary>The event type accepts a <c>segmentDefinitionId</c> filter.</summary>
public interface IFilterableBySegmentDefinition;

/// <summary>The event type accepts a <c>destinationId</c> filter.</summary>
public interface IFilterableByDestination;

/// <summary>The event type accepts an <c>endpointId</c> filter.</summary>
public interface IFilterableByEndpoint;

/// <summary>The event type accepts <c>apiVersionId</c>/<c>apiVersion</c> filters.</summary>
public interface IFilterableByApiVersion;

/// <summary>The event type accepts a <c>userId</c> filter.</summary>
public interface IFilterableByUser;

// ── Shared payload shapes ──────────────────────────────────────────────────

/// <summary>A changed source property on <see cref="SourceUpdated"/>.</summary>
public sealed record SourcePropertyChange(Sources.PropertyId Id, string Change);

/// <summary>
/// One value of a changed property, as a string; <c>Language</c> is present
/// for localized properties.
/// </summary>
public sealed record EventPropertyValue(string Value, Maybe<Sources.LanguageCode> Language);
