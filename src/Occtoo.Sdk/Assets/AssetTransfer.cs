namespace Occtoo.Assets;

/// <summary>What one byte transfer moved.</summary>
/// <param name="Key">The asset whose bytes moved.</param>
/// <param name="Bytes">How many bytes were sent.</param>
/// <param name="Elapsed">How long it took.</param>
public sealed record AssetTransfer(AssetKey Key, long Bytes, TimeSpan Elapsed);

/// <summary>
/// Configures one call to <see cref="AssetsClient.Transfer"/>.
/// </summary>
public sealed record AssetTransferOptions
{
    /// <summary>
    /// Where byte-level progress is reported. Reports arrive on the thread that
    /// produced them, so an <see cref="IProgress{T}"/> implemented directly
    /// sees them in order while <see cref="Progress{T}"/> does not.
    /// </summary>
    public IProgress<AssetProgress>? Progress { get; init; }

    /// <summary>
    /// Bounds this transfer. No limit by default: a large file on a slow line
    /// is a long request, not a stuck one, and the upload link's one-hour
    /// expiry and the caller's <see cref="CancellationToken"/> already bound
    /// it.
    /// </summary>
    /// <remarks>
    /// <see cref="OcctooClientOptions.Timeout"/> does not apply here — it
    /// bounds a call to the Occtoo API, and 100 seconds is not a sane limit for
    /// moving bytes. An upload client you supply keeps its own
    /// <see cref="HttpClient.Timeout"/> regardless of what this says; see
    /// <see cref="OcctooClientOptions.UploadHttpClient"/>.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = System.Threading.Timeout.InfiniteTimeSpan;
}
