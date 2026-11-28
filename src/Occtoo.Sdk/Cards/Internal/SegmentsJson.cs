using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.Http.Internal;
using Occtoo.Sources;

namespace Occtoo.Cards.Internal;

internal sealed record SegmentDto
{
    public required string Id { get; init; }

    public required string SegmentDefinitionId { get; init; }

    public required string CardId { get; init; }

    public string? Type { get; init; }

    public string? Status { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public string? Language { get; init; }

    public string? Image { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public JsonElement? Where { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    public DateTimeOffset? EndTime { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    internal Segment ToModel() => new(
        SegmentId.From(Id),
        Events.SegmentDefinitionId.From(SegmentDefinitionId),
        CardDefinitionId.From(CardId),
        Enums.Read<SegmentType>(Type),
        Enums.Read<SegmentStatus>(Status),
        Name,
        Description is { Length: > 0 } description ? description : Maybe<string>.None,
        LanguageCode.Read(Language),
        Image is { Length: > 0 } image ? image : Maybe<string>.None,
        Tags ?? [],
        Where is { ValueKind: JsonValueKind.Object } where ? Cards.Where.Read(where) : Maybe<Where>.None,
        StartTime is { } start ? start : Maybe<DateTimeOffset>.None,
        EndTime is { } end ? end : Maybe<DateTimeOffset>.None,
        CreatedAt,
        UpdatedAt);
}

internal sealed record CreateSegmentDto(
    string Id,
    string Name,
    string Language,
    JsonElement? Where,
    string Type,
    string? Description,
    string? Image,
    IReadOnlyList<string>? Tags,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime)
{
    internal static CreateSegmentDto From(CreateSegment segment) => new(
        segment.Id.Value,
        segment.Name.Value,
        segment.Language.Value,
        WhereJson.Element(segment.Where),
        segment.Type.ToString(),
        segment.Description.GetValueOrDefault(),
        segment.Image.GetValueOrDefault(),
        segment.Tags.GetValueOrDefault(),
        segment.StartTime.Map(time => (DateTimeOffset?)time).GetValueOrDefault(),
        segment.EndTime.Map(time => (DateTimeOffset?)time).GetValueOrDefault());
}

internal sealed record UpdateSegmentDto(
    string? Name,
    string? Language,
    JsonElement? Where,
    string? Description,
    string? Image,
    IReadOnlyList<string>? Tags,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime)
{
    internal static UpdateSegmentDto From(UpdateSegment changes) => new(
        changes.Name.Map(name => name.Value).GetValueOrDefault(),
        changes.Language.Map(language => language.Value).GetValueOrDefault(),
        changes.Where.Map(WhereJson.Element).GetValueOrDefault(),
        changes.Description.GetValueOrDefault(),
        changes.Image.GetValueOrDefault(),
        changes.Tags.GetValueOrDefault(),
        changes.StartTime.Map(time => (DateTimeOffset?)time).GetValueOrDefault(),
        changes.EndTime.Map(time => (DateTimeOffset?)time).GetValueOrDefault());
}

internal sealed record SegmentPageDto
{
    public required SegmentDto[] Items { get; init; }

    public string? After { get; init; }

    public long? TotalCount { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SegmentDto))]
[JsonSerializable(typeof(SegmentPageDto))]
[JsonSerializable(typeof(CreateSegmentDto))]
[JsonSerializable(typeof(UpdateSegmentDto))]
internal sealed partial class SegmentsJsonContext : JsonSerializerContext;
