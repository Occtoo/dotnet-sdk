using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.Http.Internal;

namespace Occtoo.Cards.Internal;

internal sealed record RelationMatchDto(string SourceProperty, string TargetProperty)
{
    internal RelationMatch ToModel() => new(CardPropertyId.Read(SourceProperty), CardPropertyId.Read(TargetProperty));

    internal static RelationMatchDto From(RelationMatch match) => new(match.SourceProperty.Value, match.TargetProperty.Value);
}

internal sealed record RelationOrderDto(string? Type, string Property, string? TargetMatchProperty)
{
    internal RelationOrder ToModel() => new(
        Type switch
        {
            "BasedOnTargetProperty" => RelationOrderBasis.TargetProperty,
            "BasedOnSourceProperty" => RelationOrderBasis.SourceList,
            _ => Maybe<RelationOrderBasis>.None,
        },
        CardPropertyId.Read(Property),
        TargetMatchProperty is { Length: > 0 } match ? CardPropertyId.Read(match) : Maybe<CardPropertyId>.None);

    internal static Result<RelationOrderDto, OcctooError> From(RelationOrder order) =>
        order.Basis.TryGetValue(out var basis)
            ? new RelationOrderDto(
                basis == RelationOrderBasis.TargetProperty ? "BasedOnTargetProperty" : "BasedOnSourceProperty",
                order.Property.Value,
                order.TargetMatchProperty.Map(property => property.Value).GetValueOrDefault())
            : new ValidationError("A relation order needs a basis.");
}

internal sealed record RelationDefinitionDto
{
    public required string Id { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public required string SourceCardId { get; init; }

    public required string TargetCardId { get; init; }

    public required RelationMatchDto Match { get; init; }

    public string? SourceDisplayName { get; init; }

    public string? TargetDisplayName { get; init; }

    public RelationOrderDto? Order { get; init; }

    public string? Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    internal RelationDefinition ToModel() => new(
        RelationDefinitionId.Read(Id),
        Name,
        Description is { Length: > 0 } description ? description : Maybe<string>.None,
        Tags ?? [],
        CardDefinitionId.From(SourceCardId),
        CardDefinitionId.From(TargetCardId),
        Match.ToModel(),
        SourceDisplayName is { Length: > 0 } source ? source : Maybe<string>.None,
        TargetDisplayName is { Length: > 0 } target ? target : Maybe<string>.None,
        Order?.ToModel() ?? Maybe<RelationOrder>.None,
        Enums.Read<RelationDefinitionStatus>(Status),
        CreatedAt,
        UpdatedAt);
}

internal sealed record CreateRelationDefinitionDto(
    string Id,
    string Name,
    string SourceCardId,
    string TargetCardId,
    RelationMatchDto Match,
    string? Description,
    IReadOnlyList<string>? Tags,
    string? SourceDisplayName,
    string? TargetDisplayName,
    RelationOrderDto? Order);

internal sealed record UpdateRelationDefinitionDto(
    string? Name,
    string? Description,
    IReadOnlyList<string>? Tags,
    RelationMatchDto? Match,
    string? SourceDisplayName,
    string? TargetDisplayName,
    RelationOrderDto? Order);

internal sealed record RelationDefinitionPageDto
{
    public required RelationDefinitionDto[] Items { get; init; }

    public string? After { get; init; }

    public long? TotalCount { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RelationDefinitionDto))]
[JsonSerializable(typeof(RelationDefinitionPageDto))]
[JsonSerializable(typeof(CreateRelationDefinitionDto))]
[JsonSerializable(typeof(UpdateRelationDefinitionDto))]
internal sealed partial class RelationDefinitionsJsonContext : JsonSerializerContext;
