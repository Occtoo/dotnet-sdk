using CSharpFunctionalExtensions;
using Occtoo.Cards.Internal;
using Occtoo.Events;
using Occtoo.Http.Internal;

namespace Occtoo.Cards;

/// <summary>
/// Narrows and pages a card definition listing. Filters combine with AND;
/// timestamp bounds are inclusive. Keep the same filters when following a cursor.
/// </summary>
public sealed record CardDefinitionListQuery
{
    /// <summary>Case-insensitive substring of the name.</summary>
    public Maybe<string> Name { get; init; } = Maybe<string>.None;

    /// <summary>Only card definitions in this state; archived ones are listed too.</summary>
    public Maybe<CardDefinitionStatus> Status { get; init; } = Maybe<CardDefinitionStatus>.None;

    /// <summary>Lower bound on creation time.</summary>
    public Maybe<DateTimeOffset> CreatedFrom { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Upper bound on creation time.</summary>
    public Maybe<DateTimeOffset> CreatedTo { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Lower bound on last modification time.</summary>
    public Maybe<DateTimeOffset> UpdatedFrom { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Upper bound on last modification time.</summary>
    public Maybe<DateTimeOffset> UpdatedTo { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Where to read and how much.</summary>
    public PageRequest Page { get; init; } = new();
}

/// <summary>
/// The tenant's card definitions and their exports. Reached through
/// <see cref="OcctooClient.CardDefinitions"/>. Reads and exports require
/// <see cref="Authentication.OcctooScopes.ReadCards"/>, writes
/// <see cref="Authentication.OcctooScopes.WriteCards"/>.
/// </summary>
/// <remarks>
/// Changes, activations and deletes carry the <see cref="ETag"/> they were
/// based on; when the card definition changed since, they are a
/// <see cref="ConflictError"/> — read it again and retry.
/// </remarks>
public sealed class CardDefinitionsClient
{
    private const string Route = "v1/card-definitions";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    internal CardDefinitionsClient(HttpClient httpClient, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
    }

    /// <summary>Reads one page of card definitions, without their card properties.</summary>
    public Task<Result<Page<CardDefinitionSummary>, OcctooError>> List(
        CardDefinitionListQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new CardDefinitionListQuery();
        var uri = new QueryString(Route)
            .Add("name", query.Name)
            .AddEnum("status", query.Status)
            .Add("createdFrom", query.CreatedFrom)
            .Add("createdTo", query.CreatedTo)
            .Add("updatedFrom", query.UpdatedFrom)
            .Add("updatedTo", query.UpdatedTo)
            .Add(query.Page)
            .ToUri();

        return Pages.Validate(query.Page)
            .Bind(() => OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri),
                "list card definitions", CardDefinitionsJsonContext.Default.CardDefinitionPageDto, cancellationToken,
                [new("occtoo.page.limit", query.Page.Limit)]))
            .MapResponse(page => Pages.ToPage(page.Items, page.After, page.TotalCount, dto => dto.ToModel()));
    }

    /// <summary>Reads a card definition with its active card properties and pending changes.</summary>
    public Task<Result<CardDefinition, OcctooError>> Get(
        CardDefinitionId cardDefinitionId,
        CancellationToken cancellationToken = default) =>
        Send(OcctooTransport.Request(HttpMethod.Get, DefinitionUri(cardDefinitionId)), "get card definition", cardDefinitionId, cancellationToken);

    /// <summary>
    /// Saves a draft. Every card property must read an existing source; the id
    /// must be free, archived card definitions included.
    /// </summary>
    public Task<Result<CardDefinition, OcctooError>> Create(
        CreateCardDefinition definition,
        CancellationToken cancellationToken = default)
    {
        if (definition is null)
            return Task.FromResult(Result.Failure<CardDefinition, OcctooError>(new ValidationError("A card definition is required.")));

        return CardPropertyDto.From(definition.Properties)
            .Bind(properties => Send(
                OcctooTransport.Request(HttpMethod.Post, new Uri(Route, UriKind.Relative),
                    new CreateCardDefinitionDto(
                        definition.Id.Value,
                        definition.Name.Value,
                        definition.Description.GetValueOrDefault(),
                        definition.Tags,
                        definition.Display.Map(CardDisplayDto.From).GetValueOrDefault(),
                        [.. definition.PropertyGroups.Select(PropertyGroupDto.From)],
                        properties),
                    CardDefinitionsJsonContext.Default.CreateCardDefinitionDto),
                "create card definition", definition.Id, cancellationToken));
    }

    /// <summary>
    /// Saves the changes made on <paramref name="changes"/>. Pending property
    /// changes replace the saved ones as a whole — <see cref="CardDefinition.Edit"/>
    /// starts from them.
    /// </summary>
    public Task<Result<CardDefinition, OcctooError>> Update(
        UpdateCardDefinition changes,
        CancellationToken cancellationToken = default)
    {
        if (changes is null)
            return Task.FromResult(Result.Failure<CardDefinition, OcctooError>(new ValidationError("Changes are required.")));

        return (changes.PendingChanged
                ? CardPropertyDto.From(changes.Pending.Set)
                    .Map(set => Maybe.From(new CardPropertyChangesRequestDto(set, [.. changes.Pending.Remove.Select(id => id.Value)])))
                : Result.Success<Maybe<CardPropertyChangesRequestDto>, OcctooError>(Maybe<CardPropertyChangesRequestDto>.None))
            .Bind(pending => Send(
                changes.ETag.AddTo(OcctooTransport.Request(HttpMethod.Patch, DefinitionUri(changes.Id),
                    new UpdateCardDefinitionDto(
                        changes.Name.Map(name => name.Value).GetValueOrDefault(),
                        changes.Description.GetValueOrDefault(),
                        changes.Tags.GetValueOrDefault(),
                        changes.Display.Map(CardDisplayDto.From).GetValueOrDefault(),
                        changes.PropertyGroups.Map(IReadOnlyList<PropertyGroupDto> (groups) => [.. groups.Select(PropertyGroupDto.From)]).GetValueOrDefault(),
                        pending.GetValueOrDefault()),
                    CardDefinitionsJsonContext.Default.UpdateCardDefinitionDto)),
                "update card definition", changes.Id, cancellationToken));
    }

    /// <summary>
    /// Applies the pending card property changes. The card definition comes
    /// back <see cref="CardDefinitionStatus.Updating"/> while its entries are
    /// rebuilt; read it until it is <see cref="CardDefinitionStatus.Active"/>.
    /// Nothing pending is a <see cref="ValidationError"/>.
    /// </summary>
    public Task<Result<CardDefinition, OcctooError>> Activate(
        CardDefinitionId cardDefinitionId,
        ETag etag,
        CancellationToken cancellationToken = default) =>
        Send(etag.AddTo(OcctooTransport.Request(HttpMethod.Post, new Uri($"{DefinitionUri(cardDefinitionId)}/activations", UriKind.Relative))),
            "activate card definition", cardDefinitionId, cancellationToken);

    /// <summary>
    /// Deletes a card definition for good; its id is free again. One that
    /// destination endpoints, relation definitions or segments use is a
    /// <see cref="ConflictError"/>. Succeeds when it is already gone.
    /// </summary>
    public Task<UnitResult<OcctooError>> Delete(
        CardDefinitionId cardDefinitionId,
        ETag etag,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, etag.AddTo(OcctooTransport.Request(HttpMethod.Delete, DefinitionUri(cardDefinitionId))),
            "delete card definition", cancellationToken, [DefinitionTag(cardDefinitionId)]);

    /// <summary>
    /// Starts exporting entries to a file and returns the export at once; the
    /// file is written in the background — read it with <see cref="GetExport"/>.
    /// </summary>
    public Task<Result<CardExport, OcctooError>> StartExport(
        CardDefinitionId cardDefinitionId,
        StartCardExport export,
        CancellationToken cancellationToken = default)
    {
        if (export is null)
            return Task.FromResult(Result.Failure<CardExport, OcctooError>(new ValidationError("An export is required.")));

        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Post, new Uri($"{DefinitionUri(cardDefinitionId)}/exports", UriKind.Relative),
                    StartCardExportDto.From(export), CardDefinitionsJsonContext.Default.StartCardExportDto),
                "start card export", CardDefinitionsJsonContext.Default.CardExportDto, cancellationToken, [DefinitionTag(cardDefinitionId)])
            .MapResponse(dto => dto.ToModel());
    }

    /// <summary>Reads an export's status and progress, and its file once it is ready.</summary>
    public Task<Result<CardExport, OcctooError>> GetExport(
        CardDefinitionId cardDefinitionId,
        CardExportId exportId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Get, new Uri($"{DefinitionUri(cardDefinitionId)}/exports/{exportId.Value:D}", UriKind.Relative)),
                "get card export", CardDefinitionsJsonContext.Default.CardExportDto, cancellationToken, [DefinitionTag(cardDefinitionId)])
            .MapResponse(dto => dto.ToModel());

    private Task<Result<CardDefinition, OcctooError>> Send(
        HttpRequestMessage request, string operation, CardDefinitionId cardDefinitionId, CancellationToken cancellationToken) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, request, operation,
                CardDefinitionsJsonContext.Default.CardDefinitionDto, cancellationToken, [DefinitionTag(cardDefinitionId)])
            .MapResponse(dto => dto.ToModel());

    private static Uri DefinitionUri(CardDefinitionId cardDefinitionId) =>
        new($"{Route}/{Uri.EscapeDataString(cardDefinitionId.Value)}", UriKind.Relative);

    private static KeyValuePair<string, object?> DefinitionTag(CardDefinitionId cardDefinitionId) =>
        new("occtoo.card_definition.id", cardDefinitionId.Value);
}
