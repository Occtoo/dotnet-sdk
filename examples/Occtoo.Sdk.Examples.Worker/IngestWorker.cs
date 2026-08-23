using CSharpFunctionalExtensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Occtoo;
using Occtoo.Sources;

namespace Occtoo.Sdk.Examples.Worker;

/// <summary>
/// Ingests a small batch of typed entries on a fixed interval — the shape of a
/// real integration that syncs stock levels or prices from a line-of-business
/// system into Occtoo.
/// </summary>
/// <remarks>
/// The SDK already retries transient failures internally — 429s wait out their
/// <c>Retry-After</c>, the rest back off exponentially — so any error reaching
/// this worker survived those retries. The handling left to do is coarse: a
/// <see cref="TransientError"/> waits for the next tick (the interval is the
/// outer backoff), and a non-transient failure is logged as something a human
/// has to fix — retrying it would produce the same rejection forever.
/// </remarks>
internal sealed class IngestWorker(
    OcctooClient client,
    OcctooSettings settings,
    IHostApplicationLifetime lifetime,
    ILogger<IngestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fail fast: prove the credential before the first tick, and stop the
        // host if it is rejected — a worker with a bad secret can only log the
        // same failure forever.
        var authenticated = await client
            .Authenticate(stoppingToken)
            .Tap(token => logger.LogInformation(
                "Authenticated against {BaseAddress}, token expires {ExpiresOn:u}",
                client.BaseAddress,
                token.HasValue ? token.Value.ExpiresOn : DateTimeOffset.MaxValue))
            .TapError(error => logger.LogCritical("Credential rejected: {Error}", error));

        if (authenticated.IsFailure)
        {
            lifetime.StopApplication();
            return;
        }

        var sourceId = SourceId.From(settings.SourceId);
        using var timer = new PeriodicTimer(settings.Interval);

        do
        {
            await IngestOnce(sourceId, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task IngestOnce(SourceId sourceId, CancellationToken stoppingToken)
    {
        var entries = ReadStockLevels();

        await client.Sources
            .IngestEntries(sourceId, entries, stoppingToken)
            .Tap(receipt => logger.LogInformation(
                "Accepted {Count} entries into '{Source}' (correlation {CorrelationId})",
                receipt.AcceptedEntryCount,
                receipt.SourceId.Value,
                receipt.CorrelationId.Value))
            .TapError(error =>
            {
                switch (error)
                {
                    case TransientError transient:
                        // Still failing after the SDK's own retries; the next
                        // tick is the outer retry, the interval the backoff.
                        logger.LogWarning("Transient failure survived retries, next tick will try again: {Error}", transient);
                        break;

                    default:
                        // Validation, authorization, a missing source: retrying
                        // reproduces the same rejection. A human has to act.
                        logger.LogError("Ingest rejected, intervention needed: {Error}", error);
                        break;
                }
            });
    }

    private static readonly string[] Skus = ["sku-100", "sku-200", "sku-300"];

    /// <summary>
    /// Stands in for the real data source — an ERP, a database, a message
    /// queue. Every entry is an upsert, so sending current state each tick is
    /// idempotent.
    /// </summary>
    private static List<SourceEntry> ReadStockLevels() =>
    [
        .. Skus.Select(sku =>
            SourceEntry.WithId(sku)
                .WithInteger("stockLevel", Random.Shared.Next(0, 500))
                .WithBoolean("inStock", true)
                .WithTimestamp("checkedAt", DateTimeOffset.UtcNow)
                .Build()),
    ];
}
