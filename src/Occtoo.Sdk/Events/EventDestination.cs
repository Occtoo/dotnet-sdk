using CSharpFunctionalExtensions;

namespace Occtoo.Events;

/// <summary>How events reach an event destination. Fixed when it is created.</summary>
public enum EventDestinationType
{
    /// <summary>HTTPS POSTs to an endpoint, signed — see <see cref="OcctooWebhook"/>.</summary>
    Webhook,

    /// <summary>Messages on an Azure Storage queue.</summary>
    AzureStorageQueue,

    /// <summary>Messages on an Azure Service Bus queue or topic.</summary>
    AzureServiceBus,

    /// <summary>Leased batches workers pull — see <see cref="EventsClient.Batch"/>.</summary>
    DurableConsumer,
}

/// <summary>Where an event destination is in its lifecycle.</summary>
public enum EventDestinationStatus
{
    /// <summary>A webhook that delivers nothing until <see cref="EventDestinationsClient.Verify"/> succeeds.</summary>
    PendingVerification,

    /// <summary>Delivering.</summary>
    Active,

    /// <summary>Stopped by <see cref="EventDestinationsClient.Pause"/>.</summary>
    Paused,

    /// <summary>Stopped by Occtoo, for instance after repeated failures.</summary>
    Disabled,

    /// <summary>Deleted.</summary>
    Deleted,
}

/// <summary>Where one delivery to a push destination is.</summary>
public enum EventDeliveryStatus
{
    /// <summary>Attempts remain.</summary>
    Pending,

    /// <summary>The target accepted the event.</summary>
    Delivered,

    /// <summary>Every attempt failed.</summary>
    DeadLettered,

    /// <summary>Cancelled by a pause, a configuration change or a delete.</summary>
    Cancelled,
}

/// <summary>
/// A tenant's event destination: where events matching its <c>Filter</c> are
/// delivered. <c>Target</c> is the address events are pushed to, without
/// credentials, and absent for a durable consumer; <c>StartsAfter</c> is absent
/// while a webhook waits for verification.
/// </summary>
public sealed record EventDestination(
    EventDestinationId Id,
    string Name,
    Maybe<EventDestinationType> Type,
    Maybe<EventDestinationStatus> Status,
    EventFilter Filter,
    Maybe<string> Target,
    Maybe<EventSequence> StartsAfter,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Starts a change to this destination; fields left alone stay as they are.</summary>
    public UpdateEventDestination Edit() => new(Id);
}

/// <summary>
/// A created destination. A webhook carries its <c>SigningSecret</c>, shown
/// only this once — store it to verify deliveries.
/// </summary>
public sealed record CreatedEventDestination(EventDestination Destination, Maybe<WebhookSigningSecret> SigningSecret);

/// <summary>One event sent to a push destination; its <c>Id</c> is the <c>webhook-id</c> a webhook receives, stable across retries.</summary>
public sealed record EventDelivery(
    Guid Id,
    string EventSource,
    string EventType,
    Guid EventId,
    EventSequence Sequence,
    Maybe<EventDeliveryStatus> Status,
    int AttemptCount,
    Maybe<int> LastResponseStatus,
    Maybe<string> LastError,
    DateTimeOffset CreatedAt,
    Maybe<DateTimeOffset> DeliveredAt);

/// <summary>
/// A published event type and its schema version. <c>FilterableProperties</c>
/// are the properties an <see cref="EventFilter"/> can narrow it by.
/// </summary>
public sealed record EventTypeDescriptor(
    string Type,
    string Version,
    string Description,
    Uri SchemaUrl,
    IReadOnlyList<string> FilterableProperties);

/// <summary>
/// A new event destination. Every branch of the filter must name an event
/// type, which <see cref="EventFilter.OfType{T}()"/> guarantees. Only events
/// after it becomes active are delivered.
/// </summary>
public sealed class CreateEventDestination
{
    private CreateEventDestination(EventDestinationName name, EventDestinationType type, EventFilter filter, EventDestinationConfiguration? configuration)
    {
        Name = name;
        Type = type;
        Filter = filter;
        Configuration = configuration;
    }

    internal EventDestinationName Name { get; }

    internal EventDestinationType Type { get; }

    internal EventFilter Filter { get; }

    internal EventDestinationConfiguration? Configuration { get; }

    /// <summary>A webhook. It starts as <see cref="EventDestinationStatus.PendingVerification"/> and delivers once verified.</summary>
    public static CreateEventDestination Webhook(EventDestinationName name, Uri endpoint, EventFilter filter) =>
        new(name, EventDestinationType.Webhook, filter, EventDestinationConfiguration.Webhook(endpoint));

    /// <summary>An Azure Storage queue, active at once.</summary>
    public static CreateEventDestination AzureStorageQueue(
        EventDestinationName name, AzureConnectionString connectionString, AzureStorageQueueName queueName, EventFilter filter) =>
        new(name, EventDestinationType.AzureStorageQueue, filter, EventDestinationConfiguration.AzureStorageQueue(connectionString, queueName));

    /// <summary>An Azure Service Bus queue or topic, active at once.</summary>
    public static CreateEventDestination AzureServiceBus(
        EventDestinationName name, AzureConnectionString connectionString, AzureServiceBusEntityName entityName, EventFilter filter) =>
        new(name, EventDestinationType.AzureServiceBus, filter, EventDestinationConfiguration.AzureServiceBus(connectionString, entityName));

    /// <summary>A durable consumer, active at once; read it with <see cref="EventsClient.Batch"/>.</summary>
    public static CreateEventDestination DurableConsumer(EventDestinationName name, EventFilter filter) =>
        new(name, EventDestinationType.DurableConsumer, filter, null);
}

/// <summary>
/// Changes to an event destination, from <see cref="EventDestination.Edit"/>.
/// A new webhook endpoint returns the webhook to
/// <see cref="EventDestinationStatus.PendingVerification"/>; new Azure
/// connection details cancel pending deliveries.
/// </summary>
public sealed record UpdateEventDestination
{
    internal UpdateEventDestination(EventDestinationId id) => Id = id;

    /// <summary>The destination to change.</summary>
    public EventDestinationId Id { get; }

    internal Maybe<EventDestinationName> Name { get; private init; }

    internal Maybe<EventFilter> Filter { get; private init; }

    internal Maybe<EventDestinationConfiguration> Configuration { get; private init; }

    /// <summary>A new display name.</summary>
    public UpdateEventDestination WithName(EventDestinationName name) => this with { Name = name };

    /// <summary>A new filter.</summary>
    public UpdateEventDestination WithFilter(EventFilter filter) => this with { Filter = filter };

    /// <summary>A new endpoint for a webhook.</summary>
    public UpdateEventDestination WithWebhookEndpoint(Uri endpoint) =>
        this with { Configuration = EventDestinationConfiguration.Webhook(endpoint) };

    /// <summary>New connection details for an Azure Storage queue.</summary>
    public UpdateEventDestination WithAzureStorageQueue(AzureConnectionString connectionString, AzureStorageQueueName queueName) =>
        this with { Configuration = EventDestinationConfiguration.AzureStorageQueue(connectionString, queueName) };

    /// <summary>New connection details for an Azure Service Bus queue or topic.</summary>
    public UpdateEventDestination WithAzureServiceBus(AzureConnectionString connectionString, AzureServiceBusEntityName entityName) =>
        this with { Configuration = EventDestinationConfiguration.AzureServiceBus(connectionString, entityName) };
}

internal sealed record EventDestinationConfiguration(string? Endpoint, string? ConnectionString, string? QueueName, string? EntityName)
{
    internal static EventDestinationConfiguration Webhook(Uri endpoint) =>
        new(endpoint?.AbsoluteUri ?? throw new ArgumentNullException(nameof(endpoint)), null, null, null);

    internal static EventDestinationConfiguration AzureStorageQueue(AzureConnectionString connectionString, AzureStorageQueueName queueName) =>
        new(null, connectionString.Value, queueName.Value, null);

    internal static EventDestinationConfiguration AzureServiceBus(AzureConnectionString connectionString, AzureServiceBusEntityName entityName) =>
        new(null, connectionString.Value, null, entityName.Value);

    // The connection string is a secret: never print it.
    public override string ToString() => $"EventDestinationConfiguration {{ Endpoint = {Endpoint}, QueueName = {QueueName}, EntityName = {EntityName} }}";
}
