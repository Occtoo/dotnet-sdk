namespace Occtoo.Events;

/// <summary>
/// Any <c>endpoint.*</c> event. <c>ApiVersion</c> is the display name behind
/// <c>ApiVersionId</c>.
/// </summary>
public abstract record EndpointEvent(
    DestinationId DestinationId,
    ApiVersionId ApiVersionId,
    string ApiVersion,
    EndpointId EndpointId)
    : CloudEvent, IFilterableByDestination, IFilterableByEndpoint, IFilterableByApiVersion;

/// <summary>A destination endpoint was created.</summary>
public sealed record EndpointCreated(
    DestinationId DestinationId,
    ApiVersionId ApiVersionId,
    string ApiVersion,
    EndpointId EndpointId) : EndpointEvent(DestinationId, ApiVersionId, ApiVersion, EndpointId);

/// <summary>A destination endpoint changed.</summary>
public sealed record EndpointUpdated(
    DestinationId DestinationId,
    ApiVersionId ApiVersionId,
    string ApiVersion,
    EndpointId EndpointId) : EndpointEvent(DestinationId, ApiVersionId, ApiVersion, EndpointId);

/// <summary>A destination endpoint was deleted.</summary>
public sealed record EndpointDeleted(
    DestinationId DestinationId,
    ApiVersionId ApiVersionId,
    string ApiVersion,
    EndpointId EndpointId) : EndpointEvent(DestinationId, ApiVersionId, ApiVersion, EndpointId);
