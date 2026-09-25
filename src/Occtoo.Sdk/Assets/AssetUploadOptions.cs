using CSharpFunctionalExtensions;

namespace Occtoo.Assets;

/// <summary>
/// Configures one call to <see cref="AssetsClient.Upload"/>.
/// </summary>
public sealed record AssetUploadOptions
{
    /// <summary>
    /// The folder the entries land in. Omitted means the data source's root
    /// folder.
    /// </summary>
    public Maybe<FolderId> FolderId { get; init; }

    /// <summary>
    /// Where each asset's progress is reported, for a caller rendering the run
    /// while it happens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The handler must be thread-safe.</b> Assets transfer in parallel, so
    /// up to <see cref="MaxConcurrentTransfers"/> threads report at once and
    /// the handler is re-entered. Guard whatever it writes into — a bare
    /// <c>List&lt;T&gt;.Add</c> loses reports or throws, and an exception
    /// thrown here leaves the run.
    /// </para>
    /// <para>
    /// Reports are made on the thread that produced them, so one asset's
    /// reports arrive in the order they happened. There is no order between
    /// assets. <see cref="Progress{T}"/> gives up even the per-asset order: it
    /// queues to the thread pool when there is no synchronization context,
    /// which is every console application, so a stale <c>Transferring</c> can
    /// land after <c>Completed</c>. Implement <see cref="IProgress{T}"/>
    /// yourself to keep it.
    /// </para>
    /// </remarks>
    public IProgress<AssetProgress>? Progress { get; init; }

    /// <summary>
    /// How many assets move their bytes at once. Four by default, and at most
    /// <see cref="MaxAllowedConcurrentTransfers"/>.
    /// </summary>
    public int MaxConcurrentTransfers { get; init; } = 4;

    /// <summary>
    /// The most transfers that can run at once — one per asset in the largest
    /// run <see cref="AssetsClient.Upload"/> accepts.
    /// </summary>
    public const int MaxAllowedConcurrentTransfers = AssetsClient.MaxAssetsPerInitialize;

    /// <summary>
    /// How many times one asset's bytes are sent before the SDK gives up on it.
    /// Three by default, and at most <see cref="MaxAllowedTransferAttempts"/>.
    /// </summary>
    /// <remarks>
    /// Only content that can be opened again is retried —
    /// <see cref="AssetContent.FromStream(Stream, long)"/> over a non-seekable
    /// stream gets one attempt whatever this says. An expired link is not an attempt:
    /// it is re-signed and sent again on its own.
    /// </remarks>
    public int MaxTransferAttempts { get; init; } = 3;

    /// <summary>
    /// The most attempts one asset's bytes are worth. Each failed attempt waits
    /// longer than the last, and the link being uploaded to lives an hour, so
    /// attempts past this one would spend that hour sleeping.
    /// </summary>
    public const int MaxAllowedTransferAttempts = 10;

    /// <summary>
    /// Bounds one asset's byte transfer. No limit by default — see
    /// <see cref="AssetTransferOptions.Timeout"/>, and
    /// <see cref="OcctooClientOptions.UploadHttpClient"/> for the one timeout
    /// the SDK does not own.
    /// </summary>
    public TimeSpan TransferTimeout { get; init; } = System.Threading.Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Whether these options can be used — the local check that spares the
    /// caller a round trip.
    /// </summary>
    internal UnitResult<OcctooError> Validate() => this switch
    {
        { MaxConcurrentTransfers: < 1 or > MaxAllowedConcurrentTransfers } => new ValidationError(
            $"{nameof(MaxConcurrentTransfers)} must be between 1 and {MaxAllowedConcurrentTransfers}."),
        { MaxTransferAttempts: < 1 or > MaxAllowedTransferAttempts } => new ValidationError(
            $"{nameof(MaxTransferAttempts)} must be between 1 and {MaxAllowedTransferAttempts}."),
        { TransferTimeout: var timeout } when timeout <= TimeSpan.Zero
                                              && timeout != System.Threading.Timeout.InfiniteTimeSpan =>
            new ValidationError($"{nameof(TransferTimeout)} must be positive."),
        _ => UnitResult.Success<OcctooError>(),
    };

    internal AssetTransferOptions ForTransfer(IProgress<AssetProgress>? progress) =>
        new() { Progress = progress, Timeout = TransferTimeout };
}
