using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using CSharpFunctionalExtensions;
using Occtoo.Assets.Internal;
using Occtoo.Logging;
using Occtoo.Sources;
using Occtoo.Telemetry;

namespace Occtoo.Assets;

// The whole upload, built on the same primitives a caller would use: create the
// assets, move the bytes, complete them.
public sealed partial class AssetsClient
{
    private const int TransferBackoffMilliseconds = 200;
    private const long MaxBackoffMilliseconds = 30_000;

    /// <summary>
    /// The longest the SDK waits out a <c>Retry-After</c> from storage before
    /// sending an asset's bytes again. A fixed ceiling of its own, because the
    /// wait holds one of <see cref="AssetUploadOptions.MaxConcurrentTransfers"/>
    /// slots against a link that expires in an hour, and storage is under no
    /// obligation to ask for something sane.
    /// </summary>
    private static readonly TimeSpan _maxHonouredRetryAfter = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Carried on the last report of an asset that had not settled when the
    /// caller cancelled the run. The run itself still leaves <see cref="Upload"/>
    /// as an <see cref="OperationCanceledException"/>.
    /// </summary>
    private static readonly CancelledError _cancelled = new("The upload run was cancelled.");

    /// <summary>
    /// Uploads assets: creates them, sends their bytes, and completes them.
    /// </summary>
    /// <param name="dataSourceId">The Media data source the assets belong to.</param>
    /// <param name="assets">What to upload. At least one, at most <see cref="MaxAssetsPerInitialize"/>, with distinct keys.</param>
    /// <param name="options">How to run the upload; the defaults are fine.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>
    /// A report carrying one outcome per asset, in the order they were passed
    /// in. A failed result means the run could not proceed at all — a local
    /// mistake, a rejected credential, a throttled or unreachable API — and
    /// nothing was uploaded.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Assets transfer several at a time, and a transfer that fails for a
    /// reason worth retrying is sent again as long as its content can be opened
    /// again. An asset gets one re-sign per run: a link the SDK can already see
    /// has expired is re-signed before any bytes go out, and a link storage
    /// rejects mid-run is re-signed with the bytes sent again — that second
    /// re-sign also needs content that can be opened again.
    /// </para>
    /// <para>
    /// Re-running a failed asset is safe: initializing a key that already
    /// exists signs a fresh link for it. Replacing the file of an asset that
    /// completed means deleting it first.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var report = await client.Assets.Upload(
    ///     SourceId.From("media"),
    ///     [
    ///         AssetUpload.FromFile("logo_1", "files/logo.png"),
    ///         AssetUpload.FromFile("note_1", "files/note.txt"),
    ///     ],
    ///     new AssetUploadOptions { Progress = dashboard });
    /// </code>
    /// </example>
    public async Task<Result<AssetUploadReport, OcctooError>> Upload(
        SourceId dataSourceId,
        IReadOnlyCollection<AssetUpload> assets,
        AssetUploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ();

        var planned = Plan(assets, options);

        using var activity = OcctooTelemetry.Source.StartActivity(
            "upload assets", ActivityKind.Client);
        activity?.SetTag("occtoo.source.id", dataSourceId.Value);
        activity?.SetTag("occtoo.assets.count", assets.Count);

        var outcome = await planned
            .Match(
                lengths => Run(dataSourceId, assets, lengths, options, cancellationToken),
                error => Task.FromResult(Result.Failure<AssetUploadReport, OcctooError>(error)))
            .ConfigureAwait(false);

        return outcome
            .Tap(report =>
            {
                var completed = report.Completed.Count;
                var failed = report.Failed.Count;

                activity?.SetTag("occtoo.assets.completed", completed);
                activity?.SetTag("occtoo.assets.failed", failed);
                OcctooLog.UploadFinished(_logger, completed, failed, dataSourceId.Value);
            })
            .TapError(error =>
            {
                OcctooTelemetry.Fail(activity, error);
                OcctooLog.UploadFailed(_logger, dataSourceId.Value, error);
            });
    }

    /// <summary>
    /// Everything that can be settled without the network: the options, the
    /// batch, and how many bytes each asset holds — which is also what the
    /// transfer needs and what progress is measured against.
    /// </summary>
    private static Result<Dictionary<AssetKey, long>, OcctooError> Plan(
        IReadOnlyCollection<AssetUpload>? assets,
        AssetUploadOptions options) =>
        options.Validate()
            .Bind(() => assets is null or { Count: 0 }
                ? Result.Failure<IReadOnlyCollection<AssetUpload>, OcctooError>(
                    new ValidationError("At least one asset is required."))
                : Result.Success<IReadOnlyCollection<AssetUpload>, OcctooError>(assets))
            .Ensure(
                present => present.Count <= MaxAssetsPerInitialize,
                new ValidationError($"At most {MaxAssetsPerInitialize} assets can be uploaded in one run."))
            .Ensure(
                present => !HasDuplicates(
                    [.. present.Select(asset => asset.Key.Value)], StringComparer.OrdinalIgnoreCase),
                new ValidationError("Asset keys must be distinct within a run; Occtoo compares them ignoring case."))
            .Bind(Lengths);

    private static Result<Dictionary<AssetKey, long>, OcctooError> Lengths(IReadOnlyCollection<AssetUpload> assets)
    {
        var lengths = new Dictionary<AssetKey, long>();

        foreach (var asset in assets)
        {
            var length = ResolveLength(asset.Content)
                .MapError(OcctooError (error) => new ValidationError($"'{asset.Key.Value}': {error.Message}"));

            if (length.IsFailure)
                return length.Error;

            lengths[asset.Key] = length.Value;
        }

        return lengths;
    }

    private Task<Result<AssetUploadReport, OcctooError>> Run(
        SourceId dataSourceId,
        IReadOnlyCollection<AssetUpload> assets,
        IReadOnlyDictionary<AssetKey, long> lengths,
        AssetUploadOptions options,
        CancellationToken cancellationToken)
    {
        foreach (var asset in assets)
            Report(options, asset, AssetUploadStage.Initializing, 0, lengths[asset.Key], null);

        return Initialize(
                dataSourceId,
                [.. assets.Select(asset => asset.Asset)],
                options.FolderId,
                cancellationToken)
            // A run that cannot start still owes every asset the report that
            // ends it: a view rendered from progress would otherwise leave the
            // whole batch sitting at "initializing" for good.
            .TapError(error =>
            {
                foreach (var asset in assets)
                    Report(options, asset, AssetUploadStage.Initializing, 0, lengths[asset.Key], error);
            })
            .Bind(async Task<Result<AssetUploadReport, OcctooError>> (batch) =>
                await Carry(dataSourceId, assets, lengths, batch, options, cancellationToken)
                    .ConfigureAwait(false));
    }

    /// <summary>
    /// From signed links to files: everything after initializing, with each
    /// asset's fate recorded as it is settled.
    /// </summary>
    private async Task<Result<AssetUploadReport, OcctooError>> Carry(
        SourceId dataSourceId,
        IReadOnlyCollection<AssetUpload> assets,
        IReadOnlyDictionary<AssetKey, long> lengths,
        AssetBatch<AssetUploadLink> initialized,
        AssetUploadOptions options,
        CancellationToken cancellationToken)
    {
        var outcomes = new ConcurrentDictionary<AssetKey, AssetUploadOutcome>();

        foreach (var asset in assets.Where(asset => initialized.Failed.ContainsKey(asset.Key)))
        {
            Settle(
                outcomes,
                options,
                asset,
                AssetUploadStage.Initializing,
                new AssetRejectedError(initialized.Failed[asset.Key]),
                transferred: 0,
                lengths[asset.Key]);
        }

        // Already settled means Occtoo refused the key: a key it also signed a
        // link for is not one to send bytes to.
        var signed = assets
            .Where(asset => initialized.Succeeded.ContainsKey(asset.Key) && !outcomes.ContainsKey(asset.Key))
            .ToList();

        // One tracker per asset, made before transfers start, so a run cut
        // short can still read how far each of them had got.
        var trackers = signed.ToDictionary(asset => asset.Key, _ => new TransferTracker(options.Progress));

        try
        {
            await Parallel.ForEachAsync(
                    signed,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = options.MaxConcurrentTransfers,
                        CancellationToken = cancellationToken,
                    },
                    async (asset, token) =>
                    {
                        var tracker = trackers[asset.Key];

                        // One span per file: the key is what an operator
                        // searches for.
                        using var activity = OcctooTelemetry.Source.StartActivity(
                            "transfer asset", ActivityKind.Client);
                        activity?.SetTag("occtoo.assets.key", asset.Key.Value);

                        var moved = await TransferWithRetry(
                                dataSourceId,
                                asset,
                                initialized.Succeeded[asset.Key],
                                lengths[asset.Key],
                                options,
                                tracker,
                                token)
                            .ConfigureAwait(false);

                        moved
                            .Tap(transfer => activity?.SetTag("occtoo.assets.bytes", transfer.Bytes))
                            .TapError(error =>
                            {
                                OcctooTelemetry.Fail(activity, error);

                                Settle(
                                    outcomes,
                                    options,
                                    asset,
                                    AssetUploadStage.Transferring,
                                    error,
                                    tracker.Transferred,
                                    lengths[asset.Key]);
                            });
                    })
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SettleCancelled(AssetUploadStage.Transferring);
            throw;
        }

        var uploaded = signed.Where(asset => !outcomes.ContainsKey(asset.Key)).ToList();

        try
        {
            // Occtoo completes at most half of what it initializes, so a full run
            // needs more than one completion call. The mismatch is the SDK's to
            // absorb, not the caller's.
            foreach (var chunk in uploaded.Chunk(MaxAssetsPerCompletion))
            {
                foreach (var asset in chunk)
                    Report(options, asset, AssetUploadStage.Completing, lengths[asset.Key], lengths[asset.Key], null);

                var completion = await Complete(
                        dataSourceId,
                        [.. chunk.Select(asset => asset.Asset)],
                        cancellationToken)
                    .ConfigureAwait(false);

                completion.Match(
                    completed => Record(outcomes, options, chunk, completed, lengths),
                    error =>
                    {
                        foreach (var asset in chunk)
                        {
                            Settle(
                                outcomes,
                                options,
                                asset,
                                AssetUploadStage.Completing,
                                error,
                                lengths[asset.Key],
                                lengths[asset.Key]);
                        }
                    });
            }
        }
        catch (OperationCanceledException)
        {
            SettleCancelled(AssetUploadStage.Completing);
            throw;
        }

        // An asset Occtoo mentioned in neither map still needs an answer, and
        // a caller watching the run still needs to be told it is over.
        foreach (var asset in assets.Where(asset => !outcomes.ContainsKey(asset.Key)))
        {
            Settle(
                outcomes,
                options,
                asset,
                AssetUploadStage.Initializing,
                new AssetRejectedError("Occtoo did not answer for this asset."),
                transferred: 0,
                lengths[asset.Key]);
        }

        return Result.Success<AssetUploadReport, OcctooError>(new AssetUploadReport(
            dataSourceId,
            [.. assets.Select(asset => outcomes[asset.Key])]));

        // Cancellation leaves the run as an exception, which is the SDK's
        // contract, but every asset still owes the report that ends it —
        // otherwise a view rendered from progress sits on its last stage for
        // good.
        void SettleCancelled(AssetUploadStage reached)
        {
            foreach (var asset in assets.Where(asset => !outcomes.ContainsKey(asset.Key)))
            {
                var tracker = trackers.GetValueOrDefault(asset.Key);

                Settle(
                    outcomes,
                    options,
                    asset,
                    tracker is null ? AssetUploadStage.Initializing : reached,
                    _cancelled,
                    tracker?.Transferred ?? 0,
                    lengths[asset.Key]);
            }
        }
    }

    private static void Record(
        ConcurrentDictionary<AssetKey, AssetUploadOutcome> outcomes,
        AssetUploadOptions options,
        IReadOnlyCollection<AssetUpload> chunk,
        AssetBatch<AssetFileInfo> completed,
        IReadOnlyDictionary<AssetKey, long> lengths)
    {
        foreach (var asset in chunk)
        {
            if (completed.Succeeded.TryGetValue(asset.Key, out var file))
            {
                outcomes[asset.Key] = new AssetUploadOutcome(
                    asset.Key,
                    asset.Filename,
                    AssetUploadStage.Completed,
                    Result.Success<AssetFileInfo, OcctooError>(file));

                Report(options, asset, AssetUploadStage.Completed, lengths[asset.Key], lengths[asset.Key], null);
                continue;
            }

            var reason = completed.Failed.TryGetValue(asset.Key, out var refused)
                ? refused
                : "Occtoo did not answer for this asset.";

            // Its bytes are all in storage; it is the file that was not made.
            Settle(
                outcomes,
                options,
                asset,
                AssetUploadStage.Completing,
                new AssetRejectedError(reason),
                lengths[asset.Key],
                lengths[asset.Key]);
        }
    }

    /// <summary>
    /// One asset's bytes, with the retries the SDK owns: a fresh link when the
    /// old one expired, another attempt when the failure was transient and the
    /// content can be read again.
    /// </summary>
    private async Task<Result<AssetTransfer, OcctooError>> TransferWithRetry(
        SourceId dataSourceId,
        AssetUpload asset,
        AssetUploadLink link,
        long length,
        AssetUploadOptions options,
        TransferTracker tracker,
        CancellationToken cancellationToken)
    {
        var transferOptions = options.ForTransfer(tracker);
        var maxAttempts = asset.Content.CanReopen ? options.MaxTransferAttempts : 1;
        var attempt = 0;
        var refreshed = false;
        var announced = false;

        Task<Result<AssetUploadLink, OcctooError>> Resign() =>
            RefreshLink(dataSourceId, asset, cancellationToken)
                .Tap(signed =>
                {
                    // One re-sign per asset per run, whether the SDK caught
                    // the expiry or storage did. It's only spent once a fresh
                    // link is in hand, so a failed refresh hasn't used up the
                    // asset's one recovery.
                    refreshed = true;
                    link = signed;
                    OcctooLog.UploadLinkRefreshed(_logger, asset.Key.Value);
                });

        // A retry loop is the one place a result's flag beats a combinator:
        // what happens next depends on the failure, and the loop carries state.
        while (true)
        {
            // Pushing a file at a link already past its expiry spends the whole
            // transfer to earn a 403. That expiry is just the local clock's
            // reading of a time the server set, though, so a re-sign that
            // doesn't succeed is no reason to refuse the upload: the bytes go
            // anyway, and if the link really was dead, the 403 below still
            // gets its re-sign.
            if (!refreshed && link.HasExpired())
                await Resign().ConfigureAwait(false);

            // One transfer-start report per asset, whatever it took to get the
            // bytes moving: a retry reports its way up from zero again, and a
            // re-signed link is not a new transfer.
            if (!announced)
            {
                announced = true;
                Report(options, asset, AssetUploadStage.Transferring, 0, length, null);
            }

            var outcome = await BlobTransfer
                .Put(_uploadHttpClient.Value, link, asset.Content, length, transferOptions, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.IsSuccess)
                return outcome.Value;

            var failure = outcome.Error;

            // An expired or invalid link is not a failed attempt — it is the
            // one failure the SDK can fix outright.
            if (failure.Status == HttpStatusCode.Forbidden
                && !refreshed
                && asset.Content.CanReopen
                && (await Resign().ConfigureAwait(false)).IsSuccess)
            {
                continue;
            }

            attempt++;
            if (attempt >= maxAttempts || failure.Error is not TransientError)
                return failure.Error;

            OcctooLog.TransferRetrying(_logger, asset.Key.Value, attempt, maxAttempts, failure.Error);
            await Task.Delay(Wait(attempt, failure.Error), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A fresh link for one asset, or why there is none — the call failing and
    /// Occtoo refusing this one key are different answers, but the caller acts
    /// on both the same way.
    /// </summary>
    private async Task<Result<AssetUploadLink, OcctooError>> RefreshLink(
        SourceId dataSourceId,
        AssetUpload asset,
        CancellationToken cancellationToken)
    {
        var refreshed = await RefreshUploadLinks(dataSourceId, [asset.Asset], cancellationToken)
            .ConfigureAwait(false);

        return refreshed.Bind(batch =>
        {
            var fresh = batch.Find(asset.Key);

            return fresh.HasValue
                ? Result.Success<AssetUploadLink, OcctooError>(fresh.Value)
                : Result.Failure<AssetUploadLink, OcctooError>(new AssetRejectedError(
                    batch.Failed.GetValueOrDefault(asset.Key, "Occtoo signed no fresh upload link for this asset.")));
        });
    }

    private static void Settle(
        ConcurrentDictionary<AssetKey, AssetUploadOutcome> outcomes,
        AssetUploadOptions options,
        AssetUpload asset,
        AssetUploadStage reached,
        OcctooError error,
        long transferred,
        long length)
    {
        outcomes[asset.Key] = new (
            asset.Key,
            asset.Filename,
            reached,
            Result.Failure<AssetFileInfo, OcctooError>(error));

        Report(options, asset, reached, transferred, length, error);
    }

    private static void Report(
        AssetUploadOptions options,
        AssetUpload asset,
        AssetUploadStage stage,
        long transferred,
        long total,
        OcctooError? failure) =>
        options.Progress?.Report(new (
            asset.Key,
            asset.Filename,
            stage,
            transferred,
            Maybe.From(total),
            failure is null ? Maybe<OcctooError>.None : Maybe.From(failure)));

    /// <summary>
    /// How long to hold this asset's concurrency slot before sending its bytes
    /// again. Storage says so itself when it is busy; otherwise the wait backs
    /// off, capped so that a retry cannot outlast the link it is retrying
    /// against.
    /// </summary>
    private static TimeSpan Wait(int attempt, OcctooError error) =>
        error is RateLimitError { RetryAfter.HasValue: true } throttled
            ? Shorter(throttled.RetryAfter.Value, _maxHonouredRetryAfter)
            : Backoff(attempt);

    private static TimeSpan Backoff(int attempt)
    {
        // Doubled in long arithmetic and capped: the shift would otherwise
        // wrap negative, and Task.Delay throws on a negative delay.
        var milliseconds = Math.Min(
            TransferBackoffMilliseconds * (1L << Math.Min(attempt - 1, 16)),
            MaxBackoffMilliseconds);

        return TimeSpan.FromMilliseconds(milliseconds * (0.8 + (Random.Shared.NextDouble() * 0.4)));
    }

    private static TimeSpan Shorter(TimeSpan left, TimeSpan right) => left < right ? left : right;

    /// <summary>
    /// Passes one asset's byte reports through and remembers how far they got,
    /// so the report that ends the asset can say how much of it moved.
    /// </summary>
    /// <remarks>
    /// One asset's transfer is sequential, so this counts on one thread at a
    /// time; the furthest count is kept rather than the last, because a retry
    /// starts the content again from zero.
    /// </remarks>
    private sealed class TransferTracker(IProgress<AssetProgress>? progress) : IProgress<AssetProgress>
    {
        internal long Transferred { get; private set; }

        public void Report(AssetProgress value)
        {
            if (value.BytesTransferred > Transferred)
                Transferred = value.BytesTransferred;

            progress?.Report(value);
        }
    }
}
