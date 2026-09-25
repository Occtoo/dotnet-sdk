using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Occtoo.Authentication;

namespace Occtoo;

/// <summary>
/// Configures an <see cref="OcctooClient"/>.
/// </summary>
public sealed record OcctooClientOptions
{
    /// <summary>
    /// The Occtoo public API, <c>https://api.occtoo.com</c>.
    /// </summary>
    public static Uri DefaultBaseAddress { get; } = new("https://api.occtoo.com");

    /// <summary>
    /// How the client authenticates. Build one with <see cref="OcctooCredential"/>.
    /// </summary>
    public required IOcctooCredential Credential { get; init; }

    /// <summary>
    /// The Occtoo API to talk to. Defaults to <see cref="DefaultBaseAddress"/>.
    /// </summary>
    public Uri BaseAddress { get; init; } = DefaultBaseAddress;

    /// <summary>
    /// Per-request timeout, applied when the client owns its
    /// <see cref="HttpClient"/>. Defaults to 100 seconds.
    /// </summary>
    /// <remarks>
    /// This does not bound the live event stream, which is long-lived by design.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// The transport asset uploads use. The SDK creates and owns one when this
    /// is left unset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Do not give it a retrying handler. An upload's body is a stream, and a
    /// handler that replays the request sends an empty one; retries belong to
    /// <see cref="Assets.AssetsClient.Upload"/>, which reopens the content
    /// first.
    /// </para>
    /// <para>
    /// A client you supply keeps its own <see cref="HttpClient.Timeout"/>, and
    /// the .NET default of 100 seconds will cut a large upload short. Set
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> and bound the
    /// transfer with <see cref="Assets.AssetUploadOptions.TransferTimeout"/>.
    /// </para>
    /// </remarks>
    public HttpClient? UploadHttpClient { get; init; }

    /// <summary>
    /// Where the SDK logs. Defaults to no logging. Everything the SDK emits
    /// lives under the <c>Occtoo</c> category prefix
    /// (<see cref="Occtoo.Logging.OcctooLogCategories"/>), so one
    /// <c>"Occtoo"</c> entry in logging configuration controls it all —
    /// <c>Information</c> for the meaningful state changes, <c>Debug</c> to
    /// trace token retrieval and ingest decisions.
    /// </summary>
    /// <remarks>
    /// <see cref="Occtoo.DependencyInjection.OcctooServiceCollectionExtensions.AddOcctooClient"/>
    /// fills this from the container automatically when it is left unset.
    /// </remarks>
    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

    /// <summary>
    /// Whether to acquire a fresh token and retry once when Occtoo answers
    /// <c>401 Unauthorized</c>. On by default.
    /// </summary>
    /// <remarks>
    /// Covers the case where a credential is revoked or rotated while a token
    /// that had not yet expired is still cached. The retry happens at most once
    /// per request, and only for token-based credentials.
    /// </remarks>
    public bool RetryOnUnauthorized { get; init; } = true;

    /// <summary>
    /// How transient failures — <c>429</c> with its <c>Retry-After</c>,
    /// <c>5xx</c>, network errors — are retried before they surface as a
    /// <see cref="TransientError"/>. On by default with three attempts of
    /// jittered exponential backoff; see <see cref="OcctooResilienceOptions"/>.
    /// </summary>
    public OcctooResilienceOptions Resilience { get; init; } = new();

    internal void Validate()
    {
        if (Credential is null)
            throw new InvalidOperationException($"{nameof(Credential)} is required.");

        if (!BaseAddress.IsAbsoluteUri)
            throw new InvalidOperationException($"{nameof(BaseAddress)} must be an absolute URI.");

        if (Timeout <= TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
            throw new InvalidOperationException($"{nameof(Timeout)} must be positive.");

        if (UploadHttpClient is { } upload && CredentialHeader(upload) is { } header)
        {
            throw new InvalidOperationException(
                $"{nameof(UploadHttpClient)} must not carry a {header} header: asset uploads go to blob "
                + "storage and are authorized by a signed URL, not by an Occtoo credential.");
        }

        Resilience.Validate();
    }

    private static string? CredentialHeader(HttpClient httpClient) => httpClient switch
    {
        { DefaultRequestHeaders.Authorization: not null } => "Authorization",
        _ when httpClient.DefaultRequestHeaders.Contains(ApiKeyCredential.HeaderName) =>
            ApiKeyCredential.HeaderName,
        _ => null,
    };
}
