using CSharpFunctionalExtensions;
using Occtoo.Cards.Internal;
using Occtoo.Http.Internal;

namespace Occtoo.Cards;

/// <summary>
/// The tenant's relation definitions — links between the cards of two card
/// definitions. Reached through <see cref="OcctooClient.RelationDefinitions"/>.
/// Reads require <see cref="Authentication.OcctooScopes.ReadCards"/>, writes
/// <see cref="Authentication.OcctooScopes.WriteCards"/>.
/// </summary>
public sealed class RelationDefinitionsClient
{
    private const string Route = "v1/relation-definitions";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    internal RelationDefinitionsClient(HttpClient httpClient, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
    }

    /// <summary>Reads one page of relation definitions, ordered by id; archived ones are left out.</summary>
    public Task<Result<Page<RelationDefinition>, OcctooError>> List(
        RelationDefinitionListQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new RelationDefinitionListQuery();
        var uri = new QueryString(Route)
            .Add("name", query.Name)
            .Add("sourceCardId", query.SourceCardDefinitionId.Map(id => id.Value))
            .Add("targetCardId", query.TargetCardDefinitionId.Map(id => id.Value))
            .AddEnum("status", query.Status)
            .Add("createdFrom", query.CreatedFrom)
            .Add("createdTo", query.CreatedTo)
            .Add("updatedFrom", query.UpdatedFrom)
            .Add("updatedTo", query.UpdatedTo)
            .Add(query.Page)
            .ToUri();

        return Pages.Validate(query.Page)
            .Bind(() => OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri),
                "list relation definitions", RelationDefinitionsJsonContext.Default.RelationDefinitionPageDto, cancellationToken,
                [new("occtoo.page.limit", query.Page.Limit)]))
            .MapResponse(page => Pages.ToPage(page.Items, page.After, page.TotalCount, dto => dto.ToModel()));
    }

    /// <summary>Reads a relation definition; it is <see cref="RelationDefinitionStatus.Active"/> once its links are built.</summary>
    public Task<Result<RelationDefinition, OcctooError>> Get(
        RelationDefinitionId relationDefinitionId,
        CancellationToken cancellationToken = default) =>
        Send(OcctooTransport.Request(HttpMethod.Get, RelationUri(relationDefinitionId)), "get relation definition", relationDefinitionId, cancellationToken);

    /// <summary>
    /// Creates a relation definition. An unknown card definition or card
    /// property is a <see cref="ValidationError"/>; a taken id a <see cref="ConflictError"/>.
    /// </summary>
    public Task<Result<RelationDefinition, OcctooError>> Create(
        CreateRelationDefinition relation,
        CancellationToken cancellationToken = default)
    {
        if (relation is null)
            return Task.FromResult(Result.Failure<RelationDefinition, OcctooError>(new ValidationError("A relation definition is required.")));

        return Order(relation.Order)
            .Bind(order => Send(
                OcctooTransport.Request(HttpMethod.Post, new Uri(Route, UriKind.Relative),
                    new CreateRelationDefinitionDto(
                        relation.Id.Value,
                        relation.Name.Value,
                        relation.SourceCardDefinitionId.Value,
                        relation.TargetCardDefinitionId.Value,
                        RelationMatchDto.From(relation.Match),
                        relation.Description.GetValueOrDefault(),
                        relation.Tags.GetValueOrDefault(),
                        relation.SourceLabel.GetValueOrDefault(),
                        relation.TargetLabel.GetValueOrDefault(),
                        order),
                    RelationDefinitionsJsonContext.Default.CreateRelationDefinitionDto),
                "create relation definition", relation.Id, cancellationToken));
    }

    /// <summary>Saves the changes made on <paramref name="changes"/>.</summary>
    public Task<Result<RelationDefinition, OcctooError>> Update(
        UpdateRelationDefinition changes,
        CancellationToken cancellationToken = default)
    {
        if (changes is null)
            return Task.FromResult(Result.Failure<RelationDefinition, OcctooError>(new ValidationError("Changes are required.")));

        return Order(changes.Order)
            .Bind(order => Send(
                OcctooTransport.Request(HttpMethod.Patch, RelationUri(changes.Id),
                    new UpdateRelationDefinitionDto(
                        changes.Name.Map(name => name.Value).GetValueOrDefault(),
                        changes.Description.GetValueOrDefault(),
                        changes.Tags.GetValueOrDefault(),
                        changes.Match.Map(RelationMatchDto.From).GetValueOrDefault(),
                        changes.SourceLabel.GetValueOrDefault(),
                        changes.TargetLabel.GetValueOrDefault(),
                        order),
                    RelationDefinitionsJsonContext.Default.UpdateRelationDefinitionDto),
                "update relation definition", changes.Id, cancellationToken));
    }

    /// <summary>
    /// Deletes a relation definition for good; its id is free again. One a
    /// destination endpoint or a segment uses is a <see cref="ConflictError"/>.
    /// Succeeds when it is already gone.
    /// </summary>
    public Task<UnitResult<OcctooError>> Delete(
        RelationDefinitionId relationDefinitionId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, RelationUri(relationDefinitionId)),
            "delete relation definition", cancellationToken, [RelationTag(relationDefinitionId)]);

    private static Result<RelationOrderDto?, OcctooError> Order(Maybe<RelationOrder> order) =>
        order.TryGetValue(out var value)
            ? RelationOrderDto.From(value).Map(RelationOrderDto? (dto) => dto)
            : Result.Success<RelationOrderDto?, OcctooError>(null);

    private Task<Result<RelationDefinition, OcctooError>> Send(
        HttpRequestMessage request, string operation, RelationDefinitionId relationDefinitionId, CancellationToken cancellationToken) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, request, operation,
                RelationDefinitionsJsonContext.Default.RelationDefinitionDto, cancellationToken, [RelationTag(relationDefinitionId)])
            .MapResponse(dto => dto.ToModel());

    private static Uri RelationUri(RelationDefinitionId relationDefinitionId) =>
        new($"{Route}/{Uri.EscapeDataString(relationDefinitionId.Value)}", UriKind.Relative);

    private static KeyValuePair<string, object?> RelationTag(RelationDefinitionId relationDefinitionId) =>
        new("occtoo.relation_definition.id", relationDefinitionId.Value);
}
