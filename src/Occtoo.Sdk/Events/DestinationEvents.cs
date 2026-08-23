using CSharpFunctionalExtensions;
using Occtoo.Sources;

namespace Occtoo.Events;

/// <summary>Any <c>destination.*</c> event.</summary>
public abstract record DestinationEvent(DestinationId DestinationId)
    : CloudEvent, IFilterableByDestination;

/// <summary>A destination was created.</summary>
public sealed record DestinationCreated(DestinationId DestinationId) : DestinationEvent(DestinationId);

/// <summary>A destination was published.</summary>
public sealed record DestinationPublished(DestinationId DestinationId) : DestinationEvent(DestinationId);

/// <summary>A destination was stopped.</summary>
public sealed record DestinationStopped(DestinationId DestinationId) : DestinationEvent(DestinationId);

/// <summary>A destination was deleted.</summary>
public sealed record DestinationDeleted(DestinationId DestinationId) : DestinationEvent(DestinationId);

/// <summary>
/// Any <c>destination_entry.*</c> event. <c>ApiVersion</c> and <c>Endpoint</c>
/// are the display names behind their ids; <c>Version</c> is the entry's
/// monotonic version.
/// </summary>
public abstract record DestinationEntryEvent(
    DestinationId DestinationId,
    ApiVersionId ApiVersionId,
    string ApiVersion,
    EndpointId EndpointId,
    string Endpoint,
    string EntryId,
    long Version)
    : CloudEvent, IFilterableByDestination, IFilterableByEndpoint, IFilterableByApiVersion;

/// <summary>
/// An entry was written to a destination endpoint. <c>Values</c> holds the
/// changed values keyed by property name, unless the payload was dropped for
/// size (<c>ValuesTruncated</c>).
/// </summary>
public sealed record DestinationEntryUpdated(
    DestinationId DestinationId,
    ApiVersionId ApiVersionId,
    string ApiVersion,
    EndpointId EndpointId,
    string Endpoint,
    string EntryId,
    long Version,
    IReadOnlyList<PropertyId> ChangedProperties,
    bool ValuesTruncated,
    Maybe<IReadOnlyDictionary<string, IReadOnlyList<EventPropertyValue>>> Values)
    : DestinationEntryEvent(DestinationId, ApiVersionId, ApiVersion, EndpointId, Endpoint, EntryId, Version);

/// <summary>An entry was removed from a destination endpoint.</summary>
public sealed record DestinationEntryDeleted(
    DestinationId DestinationId,
    ApiVersionId ApiVersionId,
    string ApiVersion,
    EndpointId EndpointId,
    string Endpoint,
    string EntryId,
    long Version)
    : DestinationEntryEvent(DestinationId, ApiVersionId, ApiVersion, EndpointId, Endpoint, EntryId, Version);
