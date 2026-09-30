using System.Text.Json;
using System.Text.Json.Serialization;
using Occtoo.Http.Internal;

namespace Occtoo.Events.Internal;

// Wire mirrors of the durable-consumer pull and acknowledge bodies. Naming
// comes from the context's Web defaults (camelCase). The events stay raw:
// EventParser maps each envelope, so one malformed event is skipped instead
// of failing the whole batch.

internal sealed record EventBatchDto
{
    public required Guid LeaseId { get; init; }

    public required int Generation { get; init; }

    public required int Attempt { get; init; }

    public required IReadOnlyList<JsonElement> Events { get; init; }
}

internal sealed record AcknowledgeEventBatchDto(Guid LeaseId, int Generation, string Status);

internal sealed record EventBatchAcknowledgementDto
{
    public string? Status { get; init; }

    public required string Committed { get; init; }

    internal EventBatchAcknowledgement ToModel() => new(
        Enums.Read<EventBatchAcknowledgementStatus>(Status),
        EventSequence.From(Committed));
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EventBatchDto))]
[JsonSerializable(typeof(AcknowledgeEventBatchDto))]
[JsonSerializable(typeof(EventBatchAcknowledgementDto))]
internal sealed partial class EventBatchJsonContext : JsonSerializerContext;
