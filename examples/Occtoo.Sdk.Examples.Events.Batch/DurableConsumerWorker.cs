using CSharpFunctionalExtensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Occtoo.Events;

namespace Occtoo.Sdk.Examples.Events.Batch;

/// <summary>
/// Runs several competing pull → process → acknowledge loops against one
/// durable consumer — the shape of a horizontally scaled consumer that mirrors
/// Occtoo changes into a search index, a cache, or a downstream system. Occtoo
/// tracks the position and hands each batch to one worker at a time, so the
/// loops keep no state of their own and any number of them (in any number of
/// processes) can share the work.
/// </summary>
/// <remarks>
/// <para>
/// Delivery is at-least-once: a batch whose lease expires, or that is
/// acknowledged <see cref="EventBatchOutcome.Failed"/>, comes back — possibly to
/// another worker. Processing must therefore be idempotent; key it on
/// <see cref="CloudEvent.Id"/>, which is stable across redeliveries.
/// </para>
/// <para>
/// The SDK already retries transient failures internally, so an error reaching
/// a loop survived those retries: a <see cref="TransientError"/> (the window of
/// unacknowledged events is full, Occtoo is briefly unavailable) waits and
/// pulls again; a paused durable consumer waits for someone to resume it; any
/// other failure is something a human has to fix, so the host stops.
/// </para>
/// </remarks>
internal sealed class DurableConsumerWorker(
    OcctooClient client,
    OcctooSettings settings,
    IHostApplicationLifetime lifetime,
    ILogger<DurableConsumerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PausedDelay = TimeSpan.FromSeconds(30);

    private readonly EventDestinationId _destinationId = EventDestinationId.From(Guid.Parse(settings.DestinationId));

    private readonly EventBatchOptions _options = new()
    {
        Limit = settings.BatchSize,
        LeaseDuration = settings.LeaseDuration,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fail fast: prove the credential before the first pull, and stop the
        // host if it is rejected.
        var authenticated = await client
            .Authenticate(stoppingToken)
            .TapError(error => logger.LogCritical("Credential rejected: {Error}", error));

        if (authenticated.IsFailure)
        {
            lifetime.StopApplication();
            return;
        }

        // One id per loop, stable across restarts: a loop that restarts after a
        // crash resumes its own abandoned lease at once instead of waiting for it
        // to expire. Two loops must never share an id — a pull from a worker
        // that holds a lease resumes that lease, so they would hand each other's
        // batches back and forth.
        var workers = Enumerable
            .Range(1, settings.Workers)
            .Select(index => EventWorkerId.From($"{Environment.MachineName}-{index}"))
            .ToArray();

        logger.LogInformation(
            "Consuming durable consumer {DestinationId} with {Count} workers.", _destinationId.Value, workers.Length);

        await Task.WhenAll(workers.Select(worker => Consume(worker, stoppingToken)));
    }

    /// <summary>
    /// One worker's loop: pull a batch, process it, acknowledge it — or wait
    /// when there is nothing to do.
    /// </summary>
    private async Task Consume(EventWorkerId worker, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pulled = await client.Events.Batch.Pull(_destinationId, worker, _options, stoppingToken);

            if (pulled.IsFailure)
            {
                if (!await WaitOut(worker, pulled.Error, stoppingToken))
                    return;

                continue;
            }

            if (pulled.Value.HasNoValue)
            {
                // Caught up: nothing new to lease right now.
                await Task.Delay(settings.IdleDelay, stoppingToken);
                continue;
            }

            await Process(worker, pulled.Value.Value);
        }
    }

    /// <summary>
    /// Handles every event in the batch and acknowledges it. A batch that has
    /// already been delivered <see cref="OcctooSettings.MaxAttempts"/> times is
    /// parked instead: failing it forever would hold the durable consumer's
    /// committed position behind it.
    /// </summary>
    private async Task Process(EventWorkerId worker, EventBatch batch)
    {
        EventBatchOutcome outcome;

        if (batch.Attempt > settings.MaxAttempts)
        {
            // A real service writes the events to a dead-letter store here, for
            // a human to inspect and replay.
            logger.LogError(
                "[{Worker}] parking lease {LeaseId} after {Attempt} attempts ({Count} events)",
                worker.Value, batch.Lease.LeaseId, batch.Attempt, batch.Events.Count);
            outcome = EventBatchOutcome.Ok;
        }
        else
        {
            try
            {
                foreach (var evt in batch.Events)
                    Handle(worker, evt);

                outcome = EventBatchOutcome.Ok;
            }
            catch (Exception exception)
            {
                // Requeued at once, and redelivered — to any worker — with a
                // higher attempt count.
                logger.LogWarning(
                    exception, "[{Worker}] lease {LeaseId} failed on attempt {Attempt}; requeueing it",
                    worker.Value, batch.Lease.LeaseId, batch.Attempt);
                outcome = EventBatchOutcome.Failed;
            }
        }

        // The work is done: acknowledge it even when the host is stopping, so a
        // shutdown does not turn finished work into a redelivery. The client's
        // request timeout still bounds the call.
        var acknowledged = await client.Events.Batch.Acknowledge(batch.Lease, outcome, CancellationToken.None);

        acknowledged
            .Tap(ack =>
            {
                if (ack.Status == EventBatchAcknowledgementStatus.Stale)
                {
                    // The lease expired while the batch was being processed and
                    // another worker now holds it — idempotent handling makes the
                    // overlap harmless. Routinely stale? Raise LeaseDuration.
                    logger.LogWarning(
                        "[{Worker}] lease {LeaseId} went stale; another worker has the batch",
                        worker.Value, batch.Lease.LeaseId);
                }
                else
                {
                    logger.LogInformation(
                        "[{Worker}] lease {LeaseId}: {Count} events {Status}; committed through {Committed}",
                        worker.Value, batch.Lease.LeaseId, batch.Events.Count,
                        ack.Status.Map(status => status.ToString().ToLowerInvariant()).GetValueOrDefault("acknowledged"),
                        ack.Committed.Value);
                }
            })
            .TapError(error =>
            {
                // Nothing is lost: the lease expires and the batch is redelivered.
                logger.LogWarning(
                    "[{Worker}] acknowledging lease {LeaseId} failed; it will be redelivered: {Error}",
                    worker.Value, batch.Lease.LeaseId, error);
            });
    }

    /// <summary>
    /// Waits out a failed pull. Returns <c>false</c> when the failure is one no
    /// amount of waiting fixes, after stopping the host.
    /// </summary>
    private async Task<bool> WaitOut(EventWorkerId worker, OcctooError error, CancellationToken stoppingToken)
    {
        switch (error)
        {
            case RateLimitError throttled:
                // The window of unacknowledged events is full: other workers'
                // acknowledgements (or expiring leases) free it up.
                logger.LogInformation("[{Worker}] throttled: {Error}", worker.Value, throttled);
                await Task.Delay(throttled.RetryAfter.GetValueOrDefault(settings.IdleDelay), stoppingToken);
                return true;

            case TransientError transient:
                logger.LogWarning("[{Worker}] pull failed transiently; retrying: {Error}", worker.Value, transient);
                await Task.Delay(settings.IdleDelay, stoppingToken);
                return true;

            case ConflictError paused:
                // Paused (or its stored filter is no longer valid): new pulls are
                // refused, and the position is kept until it is resumed.
                logger.LogWarning("[{Worker}] durable consumer unavailable: {Error}", worker.Value, paused);
                await Task.Delay(PausedDelay, stoppingToken);
                return true;

            default:
                // Revoked credential, missing scope, unknown destination, or a
                // destination that is not a durable consumer.
                logger.LogCritical("[{Worker}] pull rejected, intervention needed: {Error}", worker.Value, error);
                lifetime.StopApplication();
                return false;
        }
    }

    /// <summary>
    /// Stands in for the real work — updating a search index, invalidating a
    /// cache, notifying a downstream system. The concrete record type is the
    /// event type, so consuming is pattern matching.
    /// </summary>
    private void Handle(EventWorkerId worker, CloudEvent evt)
    {
        switch (evt)
        {
            case SourceEntryAdded added:
                logger.LogInformation(
                    "[{Worker}] [{Sequence}] entry '{Entry}' added to '{Source}'",
                    worker.Value, added.Sequence.Value, added.EntryKey.Value, added.SourceId.Value);
                break;

            case SourceEntryUpdated updated:
                logger.LogInformation(
                    "[{Worker}] [{Sequence}] entry '{Entry}' updated — {Count} properties changed",
                    worker.Value, updated.Sequence.Value, updated.EntryKey.Value, updated.ChangedProperties.Count);
                break;

            case SourceEntryDeleted deleted:
                logger.LogInformation(
                    "[{Worker}] [{Sequence}] entry '{Entry}' deleted from '{Source}'",
                    worker.Value, deleted.Sequence.Value, deleted.EntryKey.Value, deleted.SourceId.Value);
                break;

            default:
                // The destination's filter decides what arrives; log what this
                // handler does not act on rather than drop it silently.
                logger.LogInformation("[{Worker}] [{Sequence}] {Type}", worker.Value, evt.Sequence.Value, evt.Type);
                break;
        }
    }
}
