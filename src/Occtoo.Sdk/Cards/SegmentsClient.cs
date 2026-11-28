using CSharpFunctionalExtensions;
using Occtoo.Cards.Internal;
using Occtoo.Events;
using Occtoo.Http.Internal;

namespace Occtoo.Cards;

/// <summary>
/// The segments of the tenant's card definitions. Reached through
/// <see cref="OcctooClient.Segments"/>. Reads require
/// <see cref="Authentication.OcctooScopes.ReadSegments"/>, writes
/// <see cref="Authentication.OcctooScopes.WriteSegments"/>.
/// </summary>
/// <remarks>
/// A segment evaluates part of the <see cref="Where"/> language: <c>And</c> at
/// the top level, <c>Or</c> and <c>Not</c> around a single condition; text
/// <c>Eq</c>, <c>In</c> and <c>Matches</c>; list <c>Contains</c> and
/// <c>Matches</c>; integer, decimal and timestamp <c>Eq</c>, <c>In</c>,
/// <c>Gte</c> and <c>Lte</c>; boolean <c>Eq</c>; <c>HasValue</c>; and
/// <c>Related</c> by id. Anything else is a <see cref="ValidationError"/>
/// naming the limit.
/// </remarks>
public sealed class SegmentsClient
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    internal SegmentsClient(HttpClient httpClient, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
    }

    /// <summary>Reads one page of a card definition's segments; archived ones are left out.</summary>
    public Task<Result<Page<Segment>, OcctooError>> List(
        CardDefinitionId cardDefinitionId,
        SegmentListQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new SegmentListQuery();
        var uri = new QueryString(SegmentsRoute(cardDefinitionId))
            .Add("name", query.Name)
            .AddEnum("type", query.Type)
            .AddEnum("status", query.Status)
            .Add("createdFrom", query.CreatedFrom)
            .Add("createdTo", query.CreatedTo)
            .Add("updatedFrom", query.UpdatedFrom)
            .Add("updatedTo", query.UpdatedTo)
            .Add(query.Page)
            .ToUri();

        return Pages.Validate(query.Page)
            .Bind(() => OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri),
                "list segments", SegmentsJsonContext.Default.SegmentPageDto, cancellationToken,
                [DefinitionTag(cardDefinitionId), new("occtoo.page.limit", query.Page.Limit)]))
            .MapResponse(page => Pages.ToPage(page.Items, page.After, page.TotalCount, dto => dto.ToModel()));
    }

    /// <summary>Reads a segment; an archived one is a <see cref="NotFoundError"/>.</summary>
    public Task<Result<Segment, OcctooError>> Get(
        CardDefinitionId cardDefinitionId,
        SegmentId segmentId,
        CancellationToken cancellationToken = default) =>
        Send(OcctooTransport.Request(HttpMethod.Get, SegmentUri(cardDefinitionId, segmentId)), "get segment", cardDefinitionId, cancellationToken);

    /// <summary>Creates a segment; an id already taken in the card definition is a <see cref="ConflictError"/>.</summary>
    public Task<Result<Segment, OcctooError>> Create(
        CardDefinitionId cardDefinitionId,
        CreateSegment segment,
        CancellationToken cancellationToken = default) =>
        segment is null
            ? Task.FromResult(Result.Failure<Segment, OcctooError>(new ValidationError("A segment is required.")))
            : Send(OcctooTransport.Request(HttpMethod.Post, new Uri(SegmentsRoute(cardDefinitionId), UriKind.Relative),
                    CreateSegmentDto.From(segment), SegmentsJsonContext.Default.CreateSegmentDto),
                "create segment", cardDefinitionId, cancellationToken);

    /// <summary>Saves the changes made on <paramref name="changes"/>.</summary>
    public Task<Result<Segment, OcctooError>> Update(
        UpdateSegment changes,
        CancellationToken cancellationToken = default) =>
        changes is null
            ? Task.FromResult(Result.Failure<Segment, OcctooError>(new ValidationError("Changes are required.")))
            : Send(OcctooTransport.Request(HttpMethod.Patch, SegmentUri(changes.CardDefinitionId, changes.Id),
                    UpdateSegmentDto.From(changes), SegmentsJsonContext.Default.UpdateSegmentDto),
                "update segment", changes.CardDefinitionId, cancellationToken);

    /// <summary>
    /// Archives a segment. One with pending destination endpoint changes is a
    /// <see cref="ConflictError"/>; succeeds when it is already gone.
    /// </summary>
    public Task<UnitResult<OcctooError>> Delete(
        CardDefinitionId cardDefinitionId,
        SegmentId segmentId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, SegmentUri(cardDefinitionId, segmentId)),
            "delete segment", cancellationToken, [DefinitionTag(cardDefinitionId)]);

    private Task<Result<Segment, OcctooError>> Send(
        HttpRequestMessage request, string operation, CardDefinitionId cardDefinitionId, CancellationToken cancellationToken) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, request, operation,
                SegmentsJsonContext.Default.SegmentDto, cancellationToken, [DefinitionTag(cardDefinitionId)])
            .MapResponse(dto => dto.ToModel());

    private static string SegmentsRoute(CardDefinitionId cardDefinitionId) =>
        $"v1/card-definitions/{Uri.EscapeDataString(cardDefinitionId.Value)}/segments";

    private static Uri SegmentUri(CardDefinitionId cardDefinitionId, SegmentId segmentId) =>
        new($"{SegmentsRoute(cardDefinitionId)}/{Uri.EscapeDataString(segmentId.Value)}", UriKind.Relative);

    private static KeyValuePair<string, object?> DefinitionTag(CardDefinitionId cardDefinitionId) =>
        new("occtoo.card_definition.id", cardDefinitionId.Value);
}
