using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Http.Internal;

namespace Occtoo.Events.Internal;

// Wire mirrors of the event destination, delivery and event type bodies.
// Naming comes from the context's Web defaults (camelCase); enums travel as
// their names and read leniently.

internal sealed record EventDestinationDto
{
    public required Guid Id { get; init; }

    public string Name { get; init; } = "";

    public string? Type { get; init; }

    public string? Status { get; init; }

    public required string Filter { get; init; }

    public string? Target { get; init; }

    public string? StartsAfterSequence { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    internal EventDestination ToModel() => new(
        EventDestinationId.From(Id),
        Name,
        Enums.Read<EventDestinationType>(Type),
        Enums.Read<EventDestinationStatus>(Status),
        EventFilter.Raw(Filter),
        Target is { Length: > 0 } target ? target : Maybe<string>.None,
        StartsAfterSequence is { Length: > 0 } sequence ? EventSequence.From(sequence) : Maybe<EventSequence>.None,
        CreatedAt,
        UpdatedAt);
}

internal sealed record CreatedEventDestinationDto
{
    public required EventDestinationDto EventDestination { get; init; }

    public string? SigningSecret { get; init; }

    internal CreatedEventDestination ToModel() => new(
        EventDestination.ToModel(),
        SigningSecret is { Length: > 0 } secret ? WebhookSigningSecret.From(secret) : Maybe<WebhookSigningSecret>.None);
}

internal sealed record EventDeliveryDto
{
    public required Guid Id { get; init; }

    public string EventSource { get; init; } = "";

    public string EventType { get; init; } = "";

    public Guid EventId { get; init; }

    public required string Sequence { get; init; }

    public string? Status { get; init; }

    public int AttemptCount { get; init; }

    public int? LastResponseStatus { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DeliveredAt { get; init; }

    internal EventDelivery ToModel() => new(
        Id,
        EventSource,
        EventType,
        EventId,
        EventSequence.From(Sequence),
        Enums.Read<EventDeliveryStatus>(Status),
        AttemptCount,
        LastResponseStatus is { } status ? status : Maybe<int>.None,
        LastError is { Length: > 0 } error ? error : Maybe<string>.None,
        CreatedAt,
        DeliveredAt is { } delivered ? delivered : Maybe<DateTimeOffset>.None);
}

internal sealed record EventTypeDto
{
    public required string Type { get; init; }

    public string Version { get; init; } = "";

    public string Description { get; init; } = "";

    public required string SchemaUrl { get; init; }

    public IReadOnlyList<string> FilterableProperties { get; init; } = [];

    internal EventTypeDescriptor ToModel() =>
        new(Type, Version, Description, new Uri(SchemaUrl, UriKind.Absolute), FilterableProperties);
}

internal sealed record EventDestinationConfigurationDto(string? Endpoint, string? ConnectionString, string? QueueName, string? EntityName)
{
    internal static EventDestinationConfigurationDto? From(EventDestinationConfiguration? configuration) =>
        configuration is null
            ? null
            : new(configuration.Endpoint, configuration.ConnectionString, configuration.QueueName, configuration.EntityName);
}

internal sealed record CreateEventDestinationDto(
    string Name,
    EventDestinationType Type,
    string Filter,
    EventDestinationConfigurationDto? Configuration);

internal sealed record UpdateEventDestinationDto(string? Name, string? Filter, EventDestinationConfigurationDto? Configuration);

internal sealed record EventDestinationPageDto
{
    public required EventDestinationDto[] Items { get; init; }

    public string? After { get; init; }

    public long? TotalCount { get; init; }
}

internal sealed record EventDeliveryPageDto
{
    public required EventDeliveryDto[] Items { get; init; }

    public string? After { get; init; }

    public long? TotalCount { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EventDestinationDto))]
[JsonSerializable(typeof(CreatedEventDestinationDto))]
[JsonSerializable(typeof(EventDestinationPageDto))]
[JsonSerializable(typeof(EventDeliveryPageDto))]
[JsonSerializable(typeof(EventTypeDto[]))]
[JsonSerializable(typeof(CreateEventDestinationDto))]
[JsonSerializable(typeof(UpdateEventDestinationDto))]
internal sealed partial class EventDestinationsJsonContext : JsonSerializerContext;
