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
/// The typed ingest surface — <c>POST /sources/{sourceId}</c>. Reached through
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

    internal SourcesClient(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
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
            new Uri($"sources/{Uri.EscapeDataString(sourceId.Value)}", UriKind.Relative));
        request.Content = JsonContent.Create(
            new IngestRequestBody(entries),
            IngestJsonContext.Default.IngestRequestBody);

        var outcome = await Send(request, cancellationToken)
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

    private async Task<Result<HttpResponseMessage, OcctooError>> Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OcctooCredentialException exception)
        {
            return exception.Error;
        }
        catch (HttpRequestException exception)
        {
            return new NetworkError($"Could not reach Occtoo: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TimeoutError("Occtoo did not answer in time.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Handlers the consumer layered into the pipeline (a resilience
            // handler's timeout or circuit breaker, say) may throw their own
            // types. The no-throw contract holds at this boundary; only the
            // caller's own cancellation propagates.
            return new UnexpectedError($"The request failed unexpectedly: {exception.Message}");
        }
    }

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
