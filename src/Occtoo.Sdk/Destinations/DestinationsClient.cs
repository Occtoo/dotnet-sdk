using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using CSharpFunctionalExtensions;
using Occtoo.Destinations.Internal;
using Occtoo.Events;
using Occtoo.Http.Internal;

namespace Occtoo.Destinations;

/// <summary>
/// The tenant's destinations, their versions and endpoints. Reached through
/// <see cref="OcctooClient.Destinations"/>. Reads require
/// <see cref="Authentication.OcctooScopes.ReadDestinations"/>, writes
/// <see cref="Authentication.OcctooScopes.WriteDestinations"/>.
/// </summary>
/// <remarks>
/// Changing a destination, a version or an endpoint, and deleting a version,
/// carry the <see cref="ETag"/> the change was based on; when the resource
/// changed since, the call is a <see cref="ConflictError"/>.
/// </remarks>
public sealed class DestinationsClient
{
    private const string Route = "v1/destinations";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    internal DestinationsClient(HttpClient httpClient, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
    }

    // ── Destinations ───────────────────────────────────────────────────────

    /// <summary>Reads one page of destinations, ordered by id.</summary>
    public Task<Result<Page<Destination>, OcctooError>> List(
        DestinationListQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new DestinationListQuery();
        var uri = new QueryString(Route)
            .Add("name", query.Name)
            .Add("createdFrom", query.CreatedFrom)
            .Add("createdTo", query.CreatedTo)
            .Add("updatedFrom", query.UpdatedFrom)
            .Add("updatedTo", query.UpdatedTo)
            .Add(query.Page)
            .ToUri();

        return Pages.Validate(query.Page)
            .Bind(() => Send(OcctooTransport.Request(HttpMethod.Get, uri), "list destinations",
                DestinationsJsonContext.Default.PageDtoDestinationDto, [new("occtoo.page.limit", query.Page.Limit)], cancellationToken))
            .MapResponse(page => Pages.ToPage(page.Items, page.After, page.TotalCount, dto => dto.ToModel()));
    }

    /// <summary>Reads a destination.</summary>
    public Task<Result<Destination, OcctooError>> Get(DestinationId destinationId, CancellationToken cancellationToken = default) =>
        Send(OcctooTransport.Request(HttpMethod.Get, DestinationUri(destinationId)), "get destination",
                DestinationsJsonContext.Default.DestinationDto, [Tag(destinationId)], cancellationToken)
            .MapResponse(dto => dto.ToModel());

    /// <summary>
    /// Creates a destination and its first version, the way Studio does: the
    /// type becomes a tag, the version takes the authentication and languages.
    /// When the version is refused the destination stays without one —
    /// <see cref="CreateVersion"/> adds it later.
    /// </summary>
    public async Task<Result<CreatedDestination, OcctooError>> Create(
        CreateDestination destination,
        CancellationToken cancellationToken = default)
    {
        if (destination is null)
            return new ValidationError("A destination is required.");

        if (destination.Languages.Count == 0)
            return new ValidationError("The first version needs at least one language.");

        var defaultLanguage = destination.DefaultLanguage.GetValueOrDefault(destination.Languages[0].Language);
        if (destination.Languages.All(language => language.Language != defaultLanguage))
            return new ValidationError($"The default language '{defaultLanguage}' is not one of the languages.");

        var created = await Send(
                OcctooTransport.Request(HttpMethod.Post, new Uri(Route, UriKind.Relative),
                    new CreateDestinationDto(
                        destination.Id.Value,
                        destination.Name.Value,
                        destination.Description.GetValueOrDefault(),
                        DestinationTypes.WithTypeTag(destination.Tags, destination.Type)),
                    DestinationsJsonContext.Default.CreateDestinationDto),
                "create destination", DestinationsJsonContext.Default.DestinationDto, [Tag(destination.Id)], cancellationToken)
            .MapResponse(dto => dto.ToModel())
            .ConfigureAwait(false);
        if (created.IsFailure)
            return created.Error;

        return await Send(
                OcctooTransport.Request(HttpMethod.Post, VersionsUri(destination.Id),
                    new CreateDestinationVersionDto(
                        DestinationVersionDto.WireAuthentication(destination.Authentication),
                        [.. destination.Languages.Select(DestinationVersionLanguageDto.From)],
                        defaultLanguage.Value,
                        destination.AllowedOrigins,
                        null),
                    DestinationsJsonContext.Default.CreateDestinationVersionDto),
                "create destination version", DestinationsJsonContext.Default.DestinationVersionDto, [Tag(destination.Id)], cancellationToken)
            .MapResponse(dto => new CreatedDestination(created.Value, dto.ToModel(destination.Id)))
            .ConfigureAwait(false);
    }

    /// <summary>Saves the changes made on <paramref name="changes"/>.</summary>
    public Task<Result<Destination, OcctooError>> Update(UpdateDestination changes, CancellationToken cancellationToken = default) =>
        changes is null
            ? Task.FromResult(Result.Failure<Destination, OcctooError>(new ValidationError("Changes are required.")))
            : Send(changes.ETag.AddTo(OcctooTransport.Request(HttpMethod.Patch, DestinationUri(changes.Id),
                        new UpdateDestinationDto(
                            changes.Name.Map(name => name.Value).GetValueOrDefault(),
                            changes.Description.GetValueOrDefault(),
                            changes.Tags.GetValueOrDefault()),
                        DestinationsJsonContext.Default.UpdateDestinationDto)),
                    "update destination", DestinationsJsonContext.Default.DestinationDto, [Tag(changes.Id)], cancellationToken)
                .MapResponse(dto => dto.ToModel());

    /// <summary>
    /// Deletes a destination for good. One with a version not fully deleted
    /// is a <see cref="ConflictError"/>; succeeds when it is already gone.
    /// </summary>
    public Task<UnitResult<OcctooError>> Delete(DestinationId destinationId, CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, DestinationUri(destinationId)),
            "delete destination", cancellationToken, [Tag(destinationId)]);

    // ── Versions ───────────────────────────────────────────────────────────

    /// <summary>Reads one page of a destination's versions, ordered by number, without deleted ones.</summary>
    public Task<Result<Page<DestinationVersion>, OcctooError>> ListVersions(
        DestinationId destinationId,
        PageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        page ??= new PageRequest();
        var uri = new QueryString(VersionsUri(destinationId).OriginalString).Add(page).ToUri();
        return Pages.Validate(page)
            .Bind(() => Send(OcctooTransport.Request(HttpMethod.Get, uri), "list destination versions",
                DestinationsJsonContext.Default.PageDtoDestinationVersionDto, [Tag(destinationId)], cancellationToken))
            .MapResponse(result => Pages.ToPage(result.Items, result.After, result.TotalCount, dto => dto.ToModel(destinationId)));
    }

    /// <summary>Reads a version; its <c>State</c> says whether it is published.</summary>
    public Task<Result<DestinationVersion, OcctooError>> GetVersion(
        DestinationId destinationId,
        int version,
        CancellationToken cancellationToken = default) =>
        SendVersion(OcctooTransport.Request(HttpMethod.Get, VersionUri(destinationId, version)), "get destination version", destinationId, cancellationToken);

    /// <summary>
    /// Adds the next version: a copy of the latest, with its endpoints, in
    /// state <see cref="DestinationVersionState.New"/>. The latest version
    /// must be published, a <see cref="ConflictError"/> otherwise.
    /// </summary>
    public Task<Result<DestinationVersion, OcctooError>> CreateVersion(
        DestinationId destinationId,
        CancellationToken cancellationToken = default) =>
        SendVersion(OcctooTransport.Request(HttpMethod.Post, VersionsUri(destinationId)), "create destination version", destinationId, cancellationToken);

    /// <summary>Saves the changes made on <paramref name="changes"/>.</summary>
    public Task<Result<DestinationVersion, OcctooError>> UpdateVersion(
        UpdateDestinationVersion changes,
        CancellationToken cancellationToken = default) =>
        changes is null
            ? Task.FromResult(Result.Failure<DestinationVersion, OcctooError>(new ValidationError("Changes are required.")))
            : SendVersion(changes.ETag.AddTo(OcctooTransport.Request(HttpMethod.Patch, VersionUri(changes.DestinationId, changes.Version),
                    new UpdateDestinationVersionDto(
                        changes.Languages.Map(IReadOnlyList<DestinationVersionLanguageDto> (languages) => [.. languages.Select(DestinationVersionLanguageDto.From)])
                            .GetValueOrDefault(),
                        changes.DefaultLanguage.Map(language => language.Value).GetValueOrDefault(),
                        changes.AllowedOrigins.GetValueOrDefault(),
                        changes.Description.GetValueOrDefault()),
                    DestinationsJsonContext.Default.UpdateDestinationVersionDto)),
                "update destination version", changes.DestinationId, cancellationToken);

    /// <summary>
    /// Starts deleting a version and what it published; it reports
    /// <see cref="DestinationVersionState.DeleteStarted"/> until it is gone.
    /// While a publish runs it is a <see cref="ValidationError"/>.
    /// </summary>
    public Task<UnitResult<OcctooError>> DeleteVersion(
        DestinationId destinationId,
        int version,
        ETag etag,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, etag.AddTo(OcctooTransport.Request(HttpMethod.Delete, VersionUri(destinationId, version))),
            "delete destination version", cancellationToken, [Tag(destinationId)]);

    /// <summary>
    /// Publishes a new version, or the pending endpoint content of a
    /// published one. The version comes back
    /// <see cref="DestinationVersionState.Updating"/>; follow it with
    /// <see cref="GetLatestPublication"/>. A version without endpoints, one
    /// already publishing, stopped or being deleted is a <see cref="ConflictError"/>.
    /// </summary>
    public Task<Result<DestinationVersion, OcctooError>> Publish(
        DestinationId destinationId,
        int version,
        CancellationToken cancellationToken = default) =>
        SendVersion(OcctooTransport.Request(HttpMethod.Post, new Uri($"{VersionUri(destinationId, version)}/publications", UriKind.Relative)),
            "publish destination version", destinationId, cancellationToken);

    /// <summary>Reads how far the latest publish of a version is and the steps it ran.</summary>
    public Task<Result<DestinationPublication, OcctooError>> GetLatestPublication(
        DestinationId destinationId,
        int version,
        CancellationToken cancellationToken = default) =>
        Send(OcctooTransport.Request(HttpMethod.Get, new Uri($"{VersionUri(destinationId, version)}/publications/latest", UriKind.Relative)),
                "get destination publication", DestinationsJsonContext.Default.DestinationPublicationDto, [Tag(destinationId)], cancellationToken)
            .MapResponse(dto => dto.ToModel());

    // ── Endpoints ──────────────────────────────────────────────────────────

    /// <summary>Reads one page of a version's endpoints with their content, ordered by id.</summary>
    public Task<Result<Page<DestinationEndpoint>, OcctooError>> ListEndpoints(
        DestinationId destinationId,
        int version,
        PageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        page ??= new PageRequest();
        var uri = new QueryString(EndpointsUri(destinationId, version).OriginalString).Add(page).ToUri();
        return Pages.Validate(page)
            .Bind(() => Send(OcctooTransport.Request(HttpMethod.Get, uri), "list destination endpoints",
                DestinationsJsonContext.Default.PageDtoDestinationEndpointDto, [Tag(destinationId)], cancellationToken))
            .MapResponse(result => Pages.ToPage(result.Items, result.After, result.TotalCount, dto => dto.ToModel(destinationId, version)));
    }

    /// <summary>Reads an endpoint, its published content and any pending one.</summary>
    public Task<Result<DestinationEndpoint, OcctooError>> GetEndpoint(
        DestinationId destinationId,
        int version,
        EndpointId endpointId,
        CancellationToken cancellationToken = default) =>
        SendEndpoint(OcctooTransport.Request(HttpMethod.Get, EndpointUri(destinationId, version, endpointId)), "get destination endpoint",
            destinationId, version, cancellationToken);

    /// <summary>
    /// Adds an endpoint; on a published version this starts its publish. An
    /// unknown card property, relation or segment is a <see cref="ValidationError"/>;
    /// a taken id, an archived segment or a running publish a <see cref="ConflictError"/>.
    /// </summary>
    public Task<Result<DestinationEndpoint, OcctooError>> CreateEndpoint(
        DestinationId destinationId,
        int version,
        CreateDestinationEndpoint endpoint,
        CancellationToken cancellationToken = default) =>
        endpoint is null
            ? Task.FromResult(Result.Failure<DestinationEndpoint, OcctooError>(new ValidationError("An endpoint is required.")))
            : EndpointContentDto.From(endpoint.Content)
                .Bind(content => SendEndpoint(
                    OcctooTransport.Request(HttpMethod.Post, EndpointsUri(destinationId, version),
                        new CreateDestinationEndpointDto(endpoint.Id.Value, endpoint.Name.Value, content, endpoint.Description.GetValueOrDefault()),
                        DestinationsJsonContext.Default.CreateDestinationEndpointDto),
                    "create destination endpoint", destinationId, version, cancellationToken));

    /// <summary>
    /// Saves the changes made on <paramref name="changes"/>. On a published
    /// version new content waits as <c>PendingContent</c>, and removing a
    /// property, relation or facet, changing an alias, or changing the name or
    /// description is a <see cref="ConflictError"/>.
    /// </summary>
    public Task<Result<DestinationEndpoint, OcctooError>> UpdateEndpoint(
        UpdateDestinationEndpoint changes,
        CancellationToken cancellationToken = default)
    {
        if (changes is null)
            return Task.FromResult(Result.Failure<DestinationEndpoint, OcctooError>(new ValidationError("Changes are required.")));

        var content = changes.Content.TryGetValue(out var value)
            ? EndpointContentDto.From(value).Map(EndpointContentDto? (dto) => dto)
            : Result.Success<EndpointContentDto?, OcctooError>(null);

        return content.Bind(dto => SendEndpoint(
            changes.ETag.AddTo(OcctooTransport.Request(HttpMethod.Patch, EndpointUri(changes.DestinationId, changes.Version, changes.Id),
                new UpdateDestinationEndpointDto(changes.Name.Map(name => name.Value).GetValueOrDefault(), changes.Description.GetValueOrDefault(), dto),
                DestinationsJsonContext.Default.UpdateDestinationEndpointDto)),
            "update destination endpoint", changes.DestinationId, changes.Version, cancellationToken));
    }

    /// <summary>
    /// Deletes an endpoint. The last endpoint of a published version, or one
    /// while the version publishes, is a <see cref="ConflictError"/>.
    /// </summary>
    public Task<UnitResult<OcctooError>> DeleteEndpoint(
        DestinationId destinationId,
        int version,
        EndpointId endpointId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, EndpointUri(destinationId, version, endpointId)),
            "delete destination endpoint", cancellationToken, [Tag(destinationId)]);

    /// <summary>Drops the content waiting for the next publish; the published content stays.</summary>
    public Task<UnitResult<OcctooError>> DiscardPendingContent(
        DestinationId destinationId,
        int version,
        EndpointId endpointId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout,
            OcctooTransport.Request(HttpMethod.Delete, new Uri($"{EndpointUri(destinationId, version, endpointId)}/pending-content", UriKind.Relative)),
            "discard destination endpoint pending content", cancellationToken, [Tag(destinationId)]);

    private Task<Result<T, OcctooError>> Send<T>(
        HttpRequestMessage request, string operation, JsonTypeInfo<T> type, KeyValuePair<string, object?>[] tags,
        CancellationToken cancellationToken) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, request, operation, type, cancellationToken, tags);

    private Task<Result<DestinationVersion, OcctooError>> SendVersion(
        HttpRequestMessage request, string operation, DestinationId destinationId, CancellationToken cancellationToken) =>
        Send(request, operation, DestinationsJsonContext.Default.DestinationVersionDto, [Tag(destinationId)], cancellationToken)
            .MapResponse(dto => dto.ToModel(destinationId));

    private Task<Result<DestinationEndpoint, OcctooError>> SendEndpoint(
        HttpRequestMessage request, string operation, DestinationId destinationId, int version, CancellationToken cancellationToken) =>
        Send(request, operation, DestinationsJsonContext.Default.DestinationEndpointDto, [Tag(destinationId)], cancellationToken)
            .MapResponse(dto => dto.ToModel(destinationId, version));

    private static Uri DestinationUri(DestinationId destinationId) =>
        new($"{Route}/{Uri.EscapeDataString(destinationId.Value)}", UriKind.Relative);

    private static Uri VersionsUri(DestinationId destinationId) =>
        new($"{DestinationUri(destinationId)}/versions", UriKind.Relative);

    private static Uri VersionUri(DestinationId destinationId, int version) =>
        new($"{VersionsUri(destinationId)}/{version.ToString(CultureInfo.InvariantCulture)}", UriKind.Relative);

    private static Uri EndpointsUri(DestinationId destinationId, int version) =>
        new($"{VersionUri(destinationId, version)}/endpoints", UriKind.Relative);

    private static Uri EndpointUri(DestinationId destinationId, int version, EndpointId endpointId) =>
        new($"{EndpointsUri(destinationId, version)}/{Uri.EscapeDataString(endpointId.Value)}", UriKind.Relative);

    private static KeyValuePair<string, object?> Tag(DestinationId destinationId) => new("occtoo.destination.id", destinationId.Value);
}
