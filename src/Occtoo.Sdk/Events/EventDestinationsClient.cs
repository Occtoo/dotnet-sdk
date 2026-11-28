using CSharpFunctionalExtensions;
using Occtoo.Events.Internal;
using Occtoo.Http.Internal;

namespace Occtoo.Events;

/// <summary>
/// The tenant's event destinations — webhooks, Azure queues and durable
/// consumers that events are delivered to. Reached through
/// <see cref="EventsClient.Destinations"/>.
/// </summary>
/// <remarks>
/// Reads require <see cref="Authentication.OcctooScopes.ReadEventDestinations"/>,
/// writes <see cref="Authentication.OcctooScopes.WriteEventDestinations"/>.
/// </remarks>
public sealed class EventDestinationsClient
{
    private const string Route = "v1/event-destinations";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    internal EventDestinationsClient(HttpClient httpClient, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
    }

    /// <summary>Reads one page of the tenant's event destinations, ordered by name.</summary>
    public Task<Result<Page<EventDestination>, OcctooError>> List(
        PageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        page ??= new PageRequest();
        var uri = new QueryString(Route).Add(page).ToUri();

        return Pages.Validate(page)
            .Bind(() => OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri),
                "list event destinations", EventDestinationsJsonContext.Default.EventDestinationPageDto, cancellationToken,
                [new("occtoo.page.limit", page.Limit)]))
            .MapResponse(result => Pages.ToPage(result.Items, result.After, result.TotalCount, dto => dto.ToModel()));
    }

    /// <summary>Reads one event destination. Secrets and connection strings are never returned.</summary>
    public Task<Result<EventDestination, OcctooError>> Get(
        EventDestinationId destinationId,
        CancellationToken cancellationToken = default) =>
        Send(HttpMethod.Get, DestinationUri(destinationId), "get event destination", destinationId, cancellationToken);

    /// <summary>
    /// Reads one page of a push destination's recent deliveries, newest first:
    /// delivered and cancelled ones are kept for an hour, dead-lettered ones
    /// for seven days. A durable consumer has none.
    /// </summary>
    public Task<Result<Page<EventDelivery>, OcctooError>> ListDeliveries(
        EventDestinationId destinationId,
        PageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        page ??= new PageRequest();
        var uri = new QueryString($"{Route}/{destinationId.Value:D}/deliveries").Add(page).ToUri();

        return Pages.Validate(page)
            .Bind(() => OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri),
                "list event deliveries", EventDestinationsJsonContext.Default.EventDeliveryPageDto, cancellationToken,
                [DestinationTag(destinationId), new("occtoo.page.limit", page.Limit)]))
            .MapResponse(result => Pages.ToPage(result.Items, result.After, result.TotalCount, dto => dto.ToModel()));
    }

    /// <summary>
    /// Creates an event destination. A webhook comes back with its signing
    /// secret, the only time it is shown, and delivers once
    /// <see cref="Verify"/> succeeds.
    /// </summary>
    public Task<Result<CreatedEventDestination, OcctooError>> Create(
        CreateEventDestination destination,
        CancellationToken cancellationToken = default)
    {
        if (destination is null)
            return Task.FromResult(Result.Failure<CreatedEventDestination, OcctooError>(new ValidationError("An event destination is required.")));

        var body = new CreateEventDestinationDto(
            destination.Name.Value,
            destination.Type,
            destination.Filter.ToString(),
            EventDestinationConfigurationDto.From(destination.Configuration));
        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Post, new Uri(Route, UriKind.Relative), body,
                    EventDestinationsJsonContext.Default.CreateEventDestinationDto),
                "create event destination", EventDestinationsJsonContext.Default.CreatedEventDestinationDto, cancellationToken)
            .MapResponse(dto => dto.ToModel());
    }

    /// <summary>Changes the fields set on <paramref name="changes"/>; the rest stay as they are.</summary>
    public Task<Result<EventDestination, OcctooError>> Update(
        UpdateEventDestination changes,
        CancellationToken cancellationToken = default)
    {
        if (changes is null)
            return Task.FromResult(Result.Failure<EventDestination, OcctooError>(new ValidationError("Changes are required.")));

        var body = new UpdateEventDestinationDto(
            changes.Name.Map(name => name.Value).GetValueOrDefault(),
            changes.Filter.Map(filter => filter.ToString()).GetValueOrDefault(),
            EventDestinationConfigurationDto.From(changes.Configuration.GetValueOrDefault()));
        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Patch, DestinationUri(changes.Id), body,
                    EventDestinationsJsonContext.Default.UpdateEventDestinationDto),
                "update event destination", EventDestinationsJsonContext.Default.EventDestinationDto, cancellationToken,
                [DestinationTag(changes.Id)])
            .MapResponse(dto => dto.ToModel());
    }

    /// <summary>Deletes an event destination and cancels its pending deliveries. Succeeds when it is already gone.</summary>
    public Task<UnitResult<OcctooError>> Delete(
        EventDestinationId destinationId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, DestinationUri(destinationId)),
            "delete event destination", cancellationToken, [DestinationTag(destinationId)]);

    /// <summary>
    /// Sends the CloudEvents webhook handshake — an <c>OPTIONS</c> request with
    /// <c>WebHook-Request-Origin</c> — and activates the webhook when its
    /// endpoint allows the origin. A failed handshake, or a destination that
    /// is not a webhook, is a <see cref="ValidationError"/>; a webhook not
    /// pending verification is a <see cref="ConflictError"/>.
    /// </summary>
    public Task<Result<EventDestination, OcctooError>> Verify(
        EventDestinationId destinationId,
        CancellationToken cancellationToken = default) =>
        Send(HttpMethod.Post, ActionUri(destinationId, "verify"), "verify event destination", destinationId, cancellationToken);

    /// <summary>
    /// Stops delivery: a push destination's pending deliveries are cancelled,
    /// and a durable consumer hands out no new batches. A destination that
    /// isn't active is a <see cref="ConflictError"/>.
    /// </summary>
    public Task<Result<EventDestination, OcctooError>> Pause(
        EventDestinationId destinationId,
        CancellationToken cancellationToken = default) =>
        Send(HttpMethod.Post, ActionUri(destinationId, "pause"), "pause event destination", destinationId, cancellationToken);

    /// <summary>
    /// Starts delivery again: a push destination skips the events of the
    /// paused interval, a durable consumer continues where it stopped. A
    /// destination that isn't paused is a <see cref="ConflictError"/>.
    /// </summary>
    public Task<Result<EventDestination, OcctooError>> Resume(
        EventDestinationId destinationId,
        CancellationToken cancellationToken = default) =>
        Send(HttpMethod.Post, ActionUri(destinationId, "resume"), "resume event destination", destinationId, cancellationToken);

    private Task<Result<EventDestination, OcctooError>> Send(
        HttpMethod method, Uri uri, string operation, EventDestinationId destinationId, CancellationToken cancellationToken) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(method, uri), operation,
                EventDestinationsJsonContext.Default.EventDestinationDto, cancellationToken, [DestinationTag(destinationId)])
            .MapResponse(dto => dto.ToModel());

    private static Uri DestinationUri(EventDestinationId destinationId) =>
        new($"{Route}/{destinationId.Value:D}", UriKind.Relative);

    private static Uri ActionUri(EventDestinationId destinationId, string action) =>
        new($"{Route}/{destinationId.Value:D}/{action}", UriKind.Relative);

    private static KeyValuePair<string, object?> DestinationTag(EventDestinationId destinationId) =>
        new("occtoo.event_destination.id", destinationId.Value.ToString("D"));
}
