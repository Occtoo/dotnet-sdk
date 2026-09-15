using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Occtoo.Http;
using Occtoo.Http.Internal;
using Occtoo.Logging;
using Occtoo.Sources.Internal;
using Occtoo.Telemetry;

namespace Occtoo.Sources;

/// <summary>
/// The typed ingest surface — <c>POST /v1/sources/{sourceId}</c>. Reached through
/// <see cref="OcctooClient.Sources"/>.
/// </summary>
/// <remarks>
/// Requires a tenant-level application credential with the
/// <see cref="Authentication.OcctooScopes.WriteSources"/> scope and access to
/// the target source.
/// </remarks>
public sealed class SourcesClient
{
    /// <summary>
    /// Occtoo's recommended ceiling for entries in a single request. Larger
    /// batches are not rejected by the SDK, but splitting them keeps ingestion
    /// performing well.
    /// </summary>
    public const int RecommendedMaxEntriesPerRequest = 1000;

    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly TimeSpan _requestTimeout;

    internal SourcesClient(HttpClient httpClient, ILogger logger, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _logger = logger;
        _requestTimeout = requestTimeout;
    }

    /// <summary>
    /// Upserts typed entries into a source. Validation is all-or-nothing: either
    /// every entry is accepted and queued, or none are.
    /// </summary>
    /// <param name="sourceId">The source to ingest into.</param>
    /// <param name="entries">The entries to upsert. At least one.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The receipt for the accepted batch, or the <see cref="OcctooError"/>
    /// explaining the rejection — retry <see cref="TransientError"/> failures
    /// with backoff, honoring <see cref="RateLimitError.RetryAfter"/>; never
    /// retry an accepted batch.
    /// </returns>
    /// <example>
    /// <code>
    /// await client.Sources
    ///     .IngestEntries(
    ///         SourceId.From("products"),
    ///         [
    ///             SourceEntry.WithId("sku-123")
    ///                 .WithLocalizedText("name", "Blue chair", "en")
    ///                 .WithDecimal("price", 100.111m)
    ///                 .WithList("tags", "summer", "sale"),
    ///         ])
    ///     .Tap(receipt => logger.LogInformation("accepted {Id}", receipt.CorrelationId.Value))
    ///     .TapError(error => logger.LogWarning("rejected: {Error}", error));
    /// </code>
    /// </example>
    public async Task<Result<IngestReceipt, OcctooError>> IngestEntries(
        SourceId sourceId,
        IReadOnlyCollection<SourceEntry> entries,
        CancellationToken cancellationToken = default)
    {
        // Even argument mistakes are results, not exceptions — the nullable
        // annotations already warn at compile time; at runtime they stay on
        // the failure track like every other rejected input.
        if (entries is null or { Count: 0 })
            return new ValidationError("At least one entry is required.");

        using var activity = OcctooTelemetry.Source.StartActivity(
            $"ingest {sourceId.Value}", ActivityKind.Client);
        activity?.SetTag("occtoo.source.id", sourceId.Value);
        activity?.SetTag("occtoo.ingest.entry_count", entries.Count);

        OcctooLog.Ingesting(_logger, entries.Count, sourceId.Value);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"v1/sources/{Uri.EscapeDataString(sourceId.Value)}", UriKind.Relative));
        request.Content = JsonContent.Create(
            new IngestRequestBody(entries),
            IngestJsonContext.Default.IngestRequestBody);

        var outcome = await OcctooTransport
            .Send(_httpClient, _requestTimeout, request, cancellationToken)
            .Bind(async Task<Result<IngestReceipt, OcctooError>> (response) =>
            {
                using (response)
                {
                    return response.StatusCode == HttpStatusCode.Accepted
                        ? await ReadReceipt(response, cancellationToken).ConfigureAwait(false)
                        : await OcctooApiErrors
                            .Classify(response, $"Ingesting into source '{sourceId.Value}'", cancellationToken)
                            .ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

        return outcome
            .Tap(receipt =>
            {
                activity?.SetTag("occtoo.ingest.correlation_id", receipt.CorrelationId.Value);
                OcctooLog.IngestAccepted(
                    _logger, receipt.AcceptedEntryCount, receipt.SourceId.Value, receipt.CorrelationId.Value);
            })
            .TapError(error =>
            {
                OcctooTelemetry.Fail(activity, error);
                OcctooLog.IngestFailed(_logger, sourceId.Value, error);
            });
    }

    // ── Source management ──────────────────────────────────────────────────
    // Reads need read:sources, writes need write:sources; an application
    // credential also needs a grant for the source (or `sources` for all).

    /// <summary>Reads one page of the tenant's sources. Soft-deleted sources are never listed.</summary>
    public Task<Result<Page<Source>, OcctooError>> List(
        SourceListQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new SourceListQuery();
        if (Pages.Validate(query.Page) is { HasValue: true } invalid)
            return Task.FromResult(Result.Failure<Page<Source>, OcctooError>(invalid.Value));

        var uri = new QueryString("v1/sources")
            .Add("name", query.Name)
            .AddEnum("type", query.Type)
            .AddEnum("status", query.Status)
            .Add("createdFrom", query.CreatedFrom)
            .Add("createdTo", query.CreatedTo)
            .Add("updatedFrom", query.UpdatedFrom)
            .Add("updatedTo", query.UpdatedTo)
            .Add("after", query.Page.After)
            .Add("limit", query.Page.Limit)
            .ToUri();

        return OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri), "list sources",
                SourcesJsonContext.Default.ForwardPageDtoSourceDto, cancellationToken)
            .Map(page => Pages.ToPage(page.Items, page.After, page.TotalCount, dto => dto.ToModel()));
    }


    /// <summary>Reads one source's metadata.</summary>
    public Task<Result<Source, OcctooError>> Get(
        SourceId sourceId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, SourceUri(sourceId)),
                "get source", SourcesJsonContext.Default.SourceDto, cancellationToken)
            .Map(dto => dto.ToModel());

    /// <summary>
    /// Creates a generic source. Requires a grant for all sources
    /// (<c>sources</c>), not just the new id.
    /// </summary>
    public Task<Result<Source, OcctooError>> Create(
        CreateSource source,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
            return Task.FromResult(Result.Failure<Source, OcctooError>(new ValidationError("A source is required.")));

        var body = new CreateSourceDto(source.Id.Value, source.Name, source.Description.GetValueOrDefault());
        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Post, new Uri("v1/sources", UriKind.Relative), body,
                    SourcesJsonContext.Default.CreateSourceDto),
                "create source", SourcesJsonContext.Default.SourceDto, cancellationToken)
            .Map(dto => dto.ToModel());
    }

    /// <summary>Changes a source's name and/or description.</summary>
    public Task<Result<Source, OcctooError>> Update(
        SourceId sourceId,
        UpdateSource changes,
        CancellationToken cancellationToken = default)
    {
        if (changes is null)
            return Task.FromResult(Result.Failure<Source, OcctooError>(new ValidationError("Changes are required.")));

        var body = new UpdateSourceDto(changes.Name.GetValueOrDefault(), changes.Description.GetValueOrDefault());
        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Patch, SourceUri(sourceId), body, SourcesJsonContext.Default.UpdateSourceDto),
                "update source", SourcesJsonContext.Default.SourceDto, cancellationToken)
            .Map(dto => dto.ToModel());
    }

    /// <summary>
    /// Soft-deletes a source and starts its cleanup workflow. Success means
    /// the deletion was accepted, not completed.
    /// </summary>
    public Task<UnitResult<OcctooError>> Delete(
        SourceId sourceId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, SourceUri(sourceId)), "delete source", cancellationToken);

    /// <summary>Reads one page of a source's properties.</summary>
    public Task<Result<Page<SourceProperty>, OcctooError>> ListProperties(
        SourceId sourceId,
        PageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        page ??= new PageRequest();
        if (Pages.Validate(page) is { HasValue: true } invalid)
            return Task.FromResult(Result.Failure<Page<SourceProperty>, OcctooError>(invalid.Value));

        var uri = new QueryString($"v1/sources/{Uri.EscapeDataString(sourceId.Value)}/properties")
            .Add("after", page.After)
            .Add("limit", page.Limit)
            .ToUri();

        return OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri), "list source properties",
                SourcesJsonContext.Default.ForwardPageDtoSourcePropertyDto, cancellationToken)
            .Map(result => Pages.ToPage(result.Items, result.After, result.TotalCount, dto => dto.ToModel()));
    }


    /// <summary>Reads one property's metadata and workflow state.</summary>
    public Task<Result<SourceProperty, OcctooError>> GetProperty(
        SourceId sourceId,
        PropertyId propertyId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, PropertyUri(sourceId, propertyId)),
                "get source property", SourcesJsonContext.Default.SourcePropertyDto, cancellationToken)
            .Map(dto => dto.ToModel());

    /// <summary>Creates or updates a property's metadata.</summary>
    public Task<Result<SourceProperty, OcctooError>> UpsertProperty(
        SourceId sourceId,
        PropertyId propertyId,
        UpsertSourceProperty property,
        CancellationToken cancellationToken = default)
    {
        if (property is null)
            return Task.FromResult(Result.Failure<SourceProperty, OcctooError>(new ValidationError("A property is required.")));

        var body = new UpsertSourcePropertyDto(
            property.DisplayName,
            property.Type.HasValue ? property.Type.Value : null,
            property.Delimiter.GetValueOrDefault(),
            property.Description.GetValueOrDefault());

        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Put, PropertyUri(sourceId, propertyId), body,
                    SourcesJsonContext.Default.UpsertSourcePropertyDto),
                "upsert source property", SourcesJsonContext.Default.SourcePropertyDto, cancellationToken)
            .Map(dto => dto.ToModel());
    }

    /// <summary>
    /// Deletes a property through its cleanup workflow. Success means the
    /// deletion was accepted; the property reports
    /// <see cref="SourcePropertyState.Deleting"/> until it is gone.
    /// </summary>
    public Task<UnitResult<OcctooError>> DeleteProperty(
        SourceId sourceId,
        PropertyId propertyId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, PropertyUri(sourceId, propertyId)),
            "delete source property", cancellationToken);

    // ── Reading entries ────────────────────────────────────────────────────

    /// <summary>The most ids one <see cref="GetEntries"/> call may request.</summary>
    public const int MaxEntriesPerRead = 100;

    /// <summary>
    /// Reads one stored entry, with values typed by the source's current
    /// property configuration. A deleted or unknown entry is a
    /// <see cref="NotFoundError"/>; a stored value that cannot be represented
    /// as its configured type is a <see cref="ConflictError"/>.
    /// </summary>
    public Task<Result<StoredSourceEntry, OcctooError>> GetEntry(
        SourceId sourceId,
        EntryId entryId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Get, new Uri(
                    $"v1/sources/{Uri.EscapeDataString(sourceId.Value)}/entries/{Uri.EscapeDataString(entryId.Value)}",
                    UriKind.Relative)),
                "get source entry", SourceEntriesJsonContext.Default.StoredEntryDto, cancellationToken)
            .Map(dto => dto.ToModel());

    /// <summary>
    /// Reads up to <see cref="MaxEntriesPerRead"/> stored entries by id in one
    /// request. Results follow the requested order; ids that are missing or
    /// deleted are simply absent, so a shorter list is not a failure.
    /// </summary>
    public Task<Result<IReadOnlyList<StoredSourceEntry>, OcctooError>> GetEntries(
        SourceId sourceId,
        IReadOnlyList<EntryId> entryIds,
        CancellationToken cancellationToken = default)
    {
        if (entryIds is null or { Count: 0 })
            return Task.FromResult(Result.Failure<IReadOnlyList<StoredSourceEntry>, OcctooError>(
                new ValidationError("At least one entry id is required.")));

        if (entryIds.Count > MaxEntriesPerRead)
            return Task.FromResult(Result.Failure<IReadOnlyList<StoredSourceEntry>, OcctooError>(
                new ValidationError($"At most {MaxEntriesPerRead} entry ids can be read per request.")));

        var uri = new QueryString($"v1/sources/{Uri.EscapeDataString(sourceId.Value)}/entries")
            .AddEach("id", [.. entryIds.Select(id => id.Value)])
            .ToUri();

        return OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri), "get source entries",
                SourceEntriesJsonContext.Default.StoredEntriesDto, cancellationToken)
            .Map(IReadOnlyList<StoredSourceEntry> (dto) => [.. dto.Items.Select(item => item.ToModel())]);
    }

    private static Uri SourceUri(SourceId sourceId) =>
        new($"v1/sources/{Uri.EscapeDataString(sourceId.Value)}", UriKind.Relative);

    private static Uri PropertyUri(SourceId sourceId, PropertyId propertyId) =>
        new($"v1/sources/{Uri.EscapeDataString(sourceId.Value)}/properties/{Uri.EscapeDataString(propertyId.Value)}", UriKind.Relative);

    private static async Task<Result<IngestReceipt, OcctooError>> ReadReceipt(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        TypedIngestAcceptedDto? accepted;
        try
        {
            accepted = await response.Content
                .ReadFromJsonAsync(IngestJsonContext.Default.TypedIngestAcceptedDto, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            return new UnexpectedError(
                $"Occtoo accepted the batch but the receipt could not be parsed: {exception.Message}",
                (int)response.StatusCode);
        }
        catch (NotSupportedException exception)
        {
            // A non-JSON content type — a proxy or gateway answering in the
            // API's place.
            return new UnexpectedError(
                $"Occtoo accepted the batch but the receipt could not be parsed: {exception.Message}",
                (int)response.StatusCode);
        }

        if (accepted is not { SourceId.Length: > 0 })
        {
            return new UnexpectedError(
                "Occtoo accepted the batch but returned an incomplete receipt.",
                (int)response.StatusCode);
        }

        return new IngestReceipt(
            IngestCorrelationId.From(accepted.CorrelationId),
            SourceId.From(accepted.SourceId),
            accepted.AcceptedAt,
            accepted.AcceptedEntryCount,
            [
                .. (accepted.NewPropertiesFound ?? []).Select(found => new InferredProperty(
                    PropertyId.From(found.Id ?? "unknown"),
                    Enum.TryParse<SourcePropertyType>(found.Type, ignoreCase: true, out var type)
                        ? type
                        : SourcePropertyType.Text,
                    found.Delimiter is { Length: > 0 } delimiter ? delimiter : Maybe<string>.None)),
            ]);
    }
}
