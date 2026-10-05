using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Occtoo.Events.Internal;
using Occtoo.Http.Internal;
using Occtoo.Logging;

namespace Occtoo.Events;

/// <summary>
/// Durable consumers — leased batches of events that any number of stateless
/// workers pull and acknowledge, with the position tracked by Occtoo instead
/// of a cursor the caller persists. Reached through
/// <see cref="EventsClient.Batch"/>.
/// </summary>
/// <remarks>
/// <para>
/// A worker's loop is pull → process → acknowledge. A batch not acknowledged
/// within its <see cref="EventBatchOptions.LeaseDuration"/> is handed out again,
/// and a batch acknowledged <see cref="EventBatchOutcome.Failed"/> is
/// requeued at once, so delivery is at-least-once: make processing idempotent,
/// keyed on <see cref="CloudEvent.Id"/>.
/// </para>
/// <para>
/// Pull and acknowledge are safe to resend, so the client's retries cover them
/// like reads: a resent pull resumes the worker's own lease, and a resent
/// acknowledgement that already landed changes nothing — a resent
/// <see cref="EventBatchOutcome.Ok"/> reads
/// <see cref="EventBatchAcknowledgementStatus.Stale"/>, a resent
/// <see cref="EventBatchOutcome.Failed"/> reads
/// <see cref="EventBatchAcknowledgementStatus.Requeued"/> again.
/// </para>
/// <para>
/// Requires a credential with <see cref="Authentication.OcctooScopes.ReadEvents"/>
/// — the transport-specific pull and SSE scopes do not cover durable consumers.
/// </para>
/// </remarks>
public sealed class EventBatchClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly TimeSpan _requestTimeout;

    internal EventBatchClient(HttpClient httpClient, ILogger logger, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _logger = logger;
        _requestTimeout = requestTimeout;
    }

    /// <summary>
    /// Leases the next batch of events after the durable consumer's position.
    /// Expired and failed batches are redelivered before new work is handed out.
    /// </summary>
    /// <param name="destinationId">The durable consumer to pull from.</param>
    /// <param name="workerId">
    /// This worker — unique per concurrently running pull loop, and best kept
    /// stable across restarts. A pull from a worker that still holds a lease
    /// resumes that lease; see <see cref="EventWorkerId"/>.
    /// </param>
    /// <param name="options">How much to lease, and for how long; defaults to 20 events for 60 seconds.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The batch — or nothing when the durable consumer is caught up. A paused
    /// durable consumer is a <see cref="ConflictError"/>; one whose window of
    /// unacknowledged events is full is a <see cref="RateLimitError"/>, which
    /// clears as outstanding batches are acknowledged or expire. A destination
    /// that is not a durable consumer is a <see cref="ValidationError"/>.
    /// </returns>
    public Task<Result<Maybe<EventBatch>, OcctooError>> Pull(
        EventDestinationId destinationId,
        EventWorkerId workerId,
        EventBatchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new EventBatchOptions();

        return options.Validate()
            .Bind(() =>
            {
                var uri = new QueryString($"v1/event-destinations/{destinationId.Value:D}/pull")
                    .Add("limit", options.Limit)
                    .Add("visibility", options.LeaseSeconds)
                    .Add("workerId", workerId.Value)
                    .ToUri();
                var request = OcctooTransport.Request(HttpMethod.Post, uri);

                // A pull from a worker that holds a lease resumes it, so resending a
                // pull whose response was lost hands back the same batch instead of
                // leasing a second one.
                request.Options.Set(OcctooResilience.Replayable, true);

                return OcctooTransport.SendOptional(_httpClient, _requestTimeout, request, "pull event batch",
                    EventBatchJsonContext.Default.EventBatchDto, cancellationToken,
                    [
                        new("occtoo.event_destination.id", destinationId.Value.ToString("D")),
                        new("occtoo.events.worker_id", workerId.Value),
                        new("occtoo.events.limit", options.Limit),
                    ],
                    pulled => pulled.HasValue
                        ?
                        [
                            new("occtoo.events.count", pulled.Value.Events.Count),
                            new("occtoo.events.lease_id", pulled.Value.LeaseId.ToString("D")),
                            new("occtoo.events.attempt", pulled.Value.Attempt),
                        ]
                        : [new("occtoo.events.count", 0)]);
            })
            // The API throttles a durable consumer whose unacknowledged window
            // is full; the generic message blames the rate budget.
            .MapError(error => error is RateLimitError throttled
                ? throttled with
                {
                    Message = "Pulling an event batch was throttled: the durable consumer's window of "
                              + "unacknowledged events is full, or the tenant request-rate budget is exhausted. "
                              + "It clears as outstanding batches are acknowledged or their leases expire.",
                }
                : error)
            .Map(pulled => pulled.Map(dto => ToBatch(dto, destinationId)))
            .Tap(pulled =>
            {
                if (pulled.HasNoValue)
                {
                    OcctooLog.EventBatchCaughtUp(_logger, destinationId.Value);
                    return;
                }

                var batch = pulled.Value;
                OcctooLog.EventBatchPulled(
                    _logger, batch.Events.Count, destinationId.Value, batch.Lease.LeaseId, batch.Attempt);
            });
    }

    /// <summary>
    /// Settles a leased batch: <see cref="EventBatchOutcome.Ok"/> commits it,
    /// <see cref="EventBatchOutcome.Failed"/> requeues it for redelivery.
    /// Batches can still be acknowledged while the durable consumer is paused.
    /// </summary>
    /// <param name="lease">The <see cref="EventBatch.Lease"/> of the batch being settled.</param>
    /// <param name="outcome">Whether every event in the batch was processed.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// What the acknowledgement did, and the durable consumer's committed
    /// position after it. An acknowledgement for a lease that expired and was
    /// handed out again succeeds with
    /// <see cref="EventBatchAcknowledgementStatus.Stale"/> and commits nothing.
    /// </returns>
    public Task<Result<EventBatchAcknowledgement, OcctooError>> Acknowledge(
        EventBatchLease lease,
        EventBatchOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        if (lease is null)
            return Task.FromResult(Result.Failure<EventBatchAcknowledgement, OcctooError>(new ValidationError("A lease is required.")));

        var request = OcctooTransport.Request(
            HttpMethod.Post,
            new Uri($"v1/event-destinations/{lease.DestinationId.Value:D}/acknowledge", UriKind.Relative),
            new AcknowledgeEventBatchDto(lease.LeaseId, lease.Generation, outcome),
            EventBatchJsonContext.Default.AcknowledgeEventBatchDto);

        // Spans and logs name the outcome the way the wire does: "ok", "failed".
        var status = outcome.ToString().ToLowerInvariant();

        // Resending an acknowledgement that already landed changes nothing: a
        // resent ok finds the lease gone and reads stale, a resent failed
        // re-expires the already-expired lease and reads requeued.
        request.Options.Set(OcctooResilience.Replayable, true);

        return OcctooTransport.Send(_httpClient, _requestTimeout, request, "acknowledge event batch",
                EventBatchJsonContext.Default.EventBatchAcknowledgementDto, cancellationToken,
                [
                    new("occtoo.event_destination.id", lease.DestinationId.Value.ToString("D")),
                    new("occtoo.events.lease_id", lease.LeaseId.ToString("D")),
                    new("occtoo.events.outcome", status),
                ],
                acknowledged => [new("occtoo.events.acknowledgement", acknowledged.Status)])
            .MapResponse(dto => dto.ToModel())
            .Tap(acknowledged =>
            {
                if (acknowledged.Status == EventBatchAcknowledgementStatus.Stale)
                    OcctooLog.EventBatchAcknowledgementStale(_logger, lease.LeaseId, lease.DestinationId.Value);
                else
                    OcctooLog.EventBatchAcknowledged(
                        _logger, lease.LeaseId, lease.DestinationId.Value, status, acknowledged.Committed.Value);
            });
    }

    private EventBatch ToBatch(EventBatchDto dto, EventDestinationId destinationId)
    {
        // Skipping an unusable envelope matches EventsClient.Pull: acknowledging
        // the batch still commits past it, and the warning says what was skipped.
        var events = new List<CloudEvent>(dto.Events.Count);
        foreach (var envelope in dto.Events)
        {
            EventParser.Parse(envelope)
                .Tap(events.Add)
                .TapError(error => OcctooLog.EventSkipped(_logger, error.Message));
        }

        return new EventBatch(new EventBatchLease(destinationId, dto.LeaseId, dto.Generation), dto.Attempt, events);
    }
}
