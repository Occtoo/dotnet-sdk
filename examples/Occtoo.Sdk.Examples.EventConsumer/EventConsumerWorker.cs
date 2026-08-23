using CSharpFunctionalExtensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Occtoo.Events;
using Occtoo.Sources;

namespace Occtoo.Sdk.Examples.EventConsumer;

/// <summary>
/// Follows a source's entry events by polling in pages — the shape of a real
/// consumer that mirrors Occtoo changes into a search index, a cache, or a
/// downstream system. Each tick drains everything new; the cursor persisted
/// after each processed page makes a restart resume exactly where the previous
/// run stopped, processing every event once.
/// </summary>
/// <remarks>
/// The SDK already retries transient failures internally, so an error reaching
/// this worker survived those retries. A <see cref="TransientError"/> waits
/// for the next tick (the poll interval is the outer backoff) without saving a
/// cursor — the same page is simply pulled again; a non-transient failure is
/// logged as something a human has to fix.
/// </remarks>
internal sealed class EventConsumerWorker(
    OcctooClient client,
    OcctooSettings settings,
    IHostApplicationLifetime lifetime,
    ILogger<EventConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fail fast: prove the credential before the first tick, and stop the
        // host if it is rejected.
        var authenticated = await client
            .Authenticate(stoppingToken)
            .TapError(error => logger.LogCritical("Credential rejected: {Error}", error));

        if (authenticated.IsFailure)
        {
            lifetime.StopApplication();
            return;
        }

        // The filter narrows the stream server-side; the store keeps its
        // rendered form next to the cursor so a changed filter is detected and
        // the position reset (see FileCheckpointStore).
        var filter = EventFilter.OfType<SourceEntryEvent>(e => e.WithSource(settings.SourceId));
        var store = new FileCheckpointStore(settings.CheckpointPath);

        var cursor = store.Load(filter.ToString());
        logger.LogInformation(cursor.HasValue
            ? "Resuming after stored cursor."
            : "No usable checkpoint — starting from the earliest retained event.");

        using var timer = new PeriodicTimer(settings.PollInterval);

        do
        {
            cursor = await DrainOnce(filter, cursor, store, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Pulls page after page until the stream has nothing new, checkpointing
    /// after each processed page, and returns the position to resume from on
    /// the next tick.
    /// </summary>
    private async Task<Maybe<EventCursor>> DrainOnce(
        EventFilter filter,
        Maybe<EventCursor> cursor,
        FileCheckpointStore store,
        CancellationToken stoppingToken)
    {
        while (true)
        {
            var pulled = await client.Events.Pull(
                new EventQuery { Filter = filter, After = cursor, Limit = 200 },
                stoppingToken);

            if (pulled.IsFailure)
            {
                // Nothing is checkpointed for a failed page, so the next tick
                // retries the very same position — no events are skipped.
                if (pulled.Error is TransientError transient)
                    logger.LogWarning("Pull failed transiently, next tick retries: {Error}", transient);
                else
                    logger.LogError("Pull rejected, intervention needed: {Error}", pulled.Error);

                return cursor;
            }

            var page = pulled.Value;
            foreach (var evt in page.Items)
                Handle(evt);

            // Only now — with every event on the page handled — does the
            // cursor move and get persisted. A crash between pages replays
            // nothing and skips nothing.
            if (page.Next.HasValue)
            {
                cursor = page.Next;
                store.Save(page.Next.Value, filter.ToString());
            }

            if (!page.HasMore)
                return cursor;
        }
    }

    /// <summary>
    /// Stands in for the real work — updating a search index, invalidating a
    /// cache, notifying a downstream system. The concrete record type is the
    /// event type, so consuming is pattern matching.
    /// </summary>
    private void Handle(CloudEvent evt)
    {
        switch (evt)
        {
            case SourceEntryAdded added:
                logger.LogInformation(
                    "[{Sequence}] entry '{Entry}' added to '{Source}'",
                    added.Sequence.Value, added.EntryKey.Value, added.SourceId.Value);
                break;

            case SourceEntryUpdated updated:
                logger.LogInformation(
                    "[{Sequence}] entry '{Entry}' updated — {Count} properties changed",
                    updated.Sequence.Value, updated.EntryKey.Value, updated.ChangedProperties.Count);
                break;

            case SourceEntryDeleted deleted:
                logger.LogInformation(
                    "[{Sequence}] entry '{Entry}' deleted from '{Source}'",
                    deleted.Sequence.Value, deleted.EntryKey.Value, deleted.SourceId.Value);
                break;

            default:
                // The filter should preclude this, but a consumer that logs
                // rather than drops the unexpected is easier to trust.
                logger.LogInformation("[{Sequence}] {Type}", evt.Sequence.Value, evt.Type);
                break;
        }
    }
}
