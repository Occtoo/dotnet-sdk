using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Cards;
using Occtoo.Events;
using Occtoo.Http.Internal;
using Occtoo.Sources;

namespace Occtoo.Destinations.Internal;

// Wire mirrors of destinations, their versions, endpoints and publishes.
// Naming comes from the context's Web defaults (camelCase); enums read
// leniently. A version and an endpoint don't repeat their destination and
// version number, so the client passes them in.

internal sealed record DestinationDto
{
    public required string Id { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public uint Etag { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    internal Destination ToModel()
    {
        var tags = Tags ?? [];
        return new(
            DestinationId.From(Id),
            Name,
            Description is { Length: > 0 } description ? description : Maybe<string>.None,
            tags,
            DestinationTypes.Read(tags),
            ETag.From(Etag),
            CreatedAt,
            UpdatedAt);
    }
}

internal sealed record DestinationVersionLanguageDto(string Language, string SourceLanguage)
{
    internal Maybe<DestinationVersionLanguage> ToModel() =>
        LanguageCode.Read(Language).Bind(language => LanguageCode.Read(SourceLanguage)
            .Map(source => new DestinationVersionLanguage(language, source)));

    internal static DestinationVersionLanguageDto From(DestinationVersionLanguage language) =>
        new(language.Language.Value, language.SourceLanguage.Value);
}

internal sealed record DestinationVersionDto
{
    public int Version { get; init; }

    public required Guid Id { get; init; }

    public string? State { get; init; }

    public int Revision { get; init; }

    public string? Authentication { get; init; }

    public IReadOnlyList<string>? AllowedOrigins { get; init; }

    public IReadOnlyList<DestinationVersionLanguageDto>? Languages { get; init; }

    public string? DefaultLanguage { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string>? StopReason { get; init; }

    public uint Etag { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    internal DestinationVersion ToModel(DestinationId destinationId) => new(
        destinationId,
        Version,
        ApiVersionId.From(Id.ToString("D")),
        Enums.Read<DestinationVersionState>(State),
        Revision,
        Authentication switch
        {
            "Protected" => DestinationAuthentication.Protected,
            "AllowAnonymous" => DestinationAuthentication.Public,
            _ => Maybe<DestinationAuthentication>.None,
        },
        AllowedOrigins ?? [],
        [.. (Languages ?? []).Select(language => language.ToModel()).Where(language => language.HasValue).Select(language => language.Value)],
        LanguageCode.Read(DefaultLanguage),
        Description is { Length: > 0 } description ? description : Maybe<string>.None,
        StopReason ?? [],
        ETag.From(Etag),
        CreatedAt,
        UpdatedAt);

    internal static string WireAuthentication(DestinationAuthentication authentication) =>
        authentication == DestinationAuthentication.Public ? "AllowAnonymous" : "Protected";
}

internal sealed record EndpointFacetDto(int? Size, Dictionary<string, string>? Header);

internal sealed record EndpointPropertyDto(string Id, string? Alias, EndpointFacetDto? Facet);

internal sealed record EndpointSegmentsDto(IReadOnlyList<string>? Include, IReadOnlyList<string>? Exclude);

internal sealed record EndpointOrderDto(string By, string Direction);

internal sealed record EndpointRelationDto(string Id, string Alias, string? Cardinality, EndpointContentDto Content, EndpointOrderDto? Order);

internal sealed record EndpointContentDto(
    string CardId,
    IReadOnlyList<EndpointPropertyDto>? Properties,
    EndpointSegmentsDto? Segments,
    IReadOnlyList<EndpointRelationDto>? Relations)
{
    internal EndpointContent ToModel() => new(
        CardDefinitionId.From(CardId),
        [.. (Properties ?? []).Select(property => new EndpointProperty(
            CardPropertyId.Read(property.Id),
            property.Alias is { Length: > 0 } alias ? alias : Maybe<string>.None,
            property.Facet is { } facet
                ? new EndpointFacet(facet.Size ?? 50, (facet.Header ?? [])
                    .Select(pair => (Code: LanguageCode.Read(pair.Key), pair.Value))
                    .Where(pair => pair.Code.HasValue)
                    .ToDictionary(pair => pair.Code.Value, pair => pair.Value))
                : Maybe<EndpointFacet>.None))],
        [.. (Segments?.Include ?? []).Select(SegmentId.From)],
        [.. (Segments?.Exclude ?? []).Select(SegmentId.From)],
        [.. (Relations ?? []).Select(relation => new EndpointRelation(
            RelationDefinitionId.Read(relation.Id),
            relation.Alias,
            Enums.Read<EndpointCardinality>(relation.Cardinality),
            relation.Content.ToModel(),
            relation.Order is { } order
                ? new EndpointOrder(CardPropertyId.Read(order.By), order.Direction == "Desc" ? SortDirection.Descending : SortDirection.Ascending)
                : Maybe<EndpointOrder>.None))]);

    internal static Result<EndpointContentDto, OcctooError> From(EndpointContent content)
    {
        var relations = new List<EndpointRelationDto>(content.Relations.Count);
        foreach (var relation in content.Relations)
        {
            if (!relation.Cardinality.TryGetValue(out var cardinality))
                return new ValidationError($"Relation '{relation.Id}' has no cardinality.");

            var nested = From(relation.Content);
            if (nested.IsFailure)
                return nested.Error;

            relations.Add(new EndpointRelationDto(
                relation.Id.Value,
                relation.Alias,
                cardinality.ToString(),
                nested.Value,
                relation.Order.Map(order => new EndpointOrderDto(order.By.Value, order.Direction == SortDirection.Descending ? "Desc" : "Asc"))
                    .GetValueOrDefault()));
        }

        return new EndpointContentDto(
            content.CardDefinitionId.Value,
            [.. content.Properties.Select(property => new EndpointPropertyDto(
                property.Id.Value,
                property.Alias.GetValueOrDefault(),
                property.Facet.Map(facet => new EndpointFacetDto(facet.Size, facet.Header.ToDictionary(pair => pair.Key.Value, pair => pair.Value)))
                    .GetValueOrDefault()))],
            content.IncludeSegments.Count + content.ExcludeSegments.Count > 0
                ? new EndpointSegmentsDto(
                    content.IncludeSegments.Count > 0 ? [.. content.IncludeSegments.Select(id => id.Value)] : null,
                    content.ExcludeSegments.Count > 0 ? [.. content.ExcludeSegments.Select(id => id.Value)] : null)
                : null,
            relations.Count > 0 ? relations : null);
    }
}

internal sealed record DestinationEndpointDto
{
    public required string Id { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public required EndpointContentDto Content { get; init; }

    public EndpointContentDto? PendingContent { get; init; }

    public uint Etag { get; init; }

    internal DestinationEndpoint ToModel(DestinationId destinationId, int version) => new(
        destinationId,
        version,
        EndpointId.From(Id),
        Name,
        Description is { Length: > 0 } description ? description : Maybe<string>.None,
        Content.ToModel(),
        PendingContent?.ToModel() ?? Maybe<EndpointContent>.None,
        ETag.From(Etag));
}

internal sealed record DestinationPublicationStepDto(string Type, string Title, string? Status, int Attempt, DateTimeOffset Timestamp);

internal sealed record DestinationPublicationDto
{
    public string? Status { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public IReadOnlyList<DestinationPublicationStepDto>? Steps { get; init; }

    internal DestinationPublication ToModel() => new(
        Enums.Read<PublicationStatus>(Status),
        StartedAt is { } started ? started : Maybe<DateTimeOffset>.None,
        UpdatedAt is { } updated ? updated : Maybe<DateTimeOffset>.None,
        [.. (Steps ?? []).Select(step => new DestinationPublicationStep(step.Type, step.Title, Enums.Read<PublicationStatus>(step.Status), step.Attempt, step.Timestamp))]);
}

internal sealed record CreateDestinationDto(string Id, string Name, string? Description, IReadOnlyList<string> Tags);

internal sealed record UpdateDestinationDto(string? Name, string? Description, IReadOnlyList<string>? Tags);

internal sealed record CreateDestinationVersionDto(
    string Authentication,
    IReadOnlyList<DestinationVersionLanguageDto> Languages,
    string DefaultLanguage,
    IReadOnlyList<string> AllowedOrigins,
    string? Description);

internal sealed record UpdateDestinationVersionDto(
    IReadOnlyList<DestinationVersionLanguageDto>? Languages,
    string? DefaultLanguage,
    IReadOnlyList<string>? AllowedOrigins,
    string? Description);

internal sealed record CreateDestinationEndpointDto(string Id, string Name, EndpointContentDto Content, string? Description);

internal sealed record UpdateDestinationEndpointDto(string? Name, string? Description, EndpointContentDto? Content);

internal sealed record PageDto<T>
{
    public required T[] Items { get; init; }

    public string? After { get; init; }

    public long? TotalCount { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DestinationDto))]
[JsonSerializable(typeof(PageDto<DestinationDto>))]
[JsonSerializable(typeof(DestinationVersionDto))]
[JsonSerializable(typeof(PageDto<DestinationVersionDto>))]
[JsonSerializable(typeof(DestinationEndpointDto))]
[JsonSerializable(typeof(PageDto<DestinationEndpointDto>))]
[JsonSerializable(typeof(DestinationPublicationDto))]
[JsonSerializable(typeof(CreateDestinationDto))]
[JsonSerializable(typeof(UpdateDestinationDto))]
[JsonSerializable(typeof(CreateDestinationVersionDto))]
[JsonSerializable(typeof(UpdateDestinationVersionDto))]
[JsonSerializable(typeof(CreateDestinationEndpointDto))]
[JsonSerializable(typeof(UpdateDestinationEndpointDto))]
internal sealed partial class DestinationsJsonContext : JsonSerializerContext;
