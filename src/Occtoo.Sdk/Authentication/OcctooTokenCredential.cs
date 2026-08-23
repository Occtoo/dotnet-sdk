using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Occtoo.Logging;
using Occtoo.Telemetry;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Authentication;

/// <summary>
/// Base class for credentials that work by obtaining a bearer token.
/// </summary>
/// <remarks>
/// <para>
/// Handles the parts every token flow gets wrong on its own: a single in-flight
/// acquisition even under concurrent requests, reuse of a token for its whole
/// lifetime, and replacement slightly before expiry rather than after a request
/// has already failed. Derived classes only implement the flow itself.
/// </para>
/// <para>
/// Tokens are cached in a <see cref="IFusionCache"/> — in-memory by default, so
/// nothing needs configuring. Supply your own cache (through
/// <see cref="OcctooCredential"/>'s factory methods) to share tokens across
/// processes via any FusionCache second-level provider, such as Redis.
/// </para>
/// </remarks>
public abstract class OcctooTokenCredential : IOcctooCredential, IDisposable
{
    private static readonly Lazy<IFusionCache> SharedInMemoryCache = new(() =>
        new FusionCache(new FusionCacheOptions { CacheName = "Occtoo.Tokens" }));

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IFusionCache _cache;
    private readonly string _cacheKey;
    private ILogger _logger = NullLogger.Instance;
    private bool _disposed;

    /// <summary>
    /// Creates the credential.
    /// </summary>
    /// <param name="cacheKey">
    /// Identifies this credential's token in the cache. Two credentials with the
    /// same key share a token, which is what you want when they describe the
    /// same identity — and never otherwise.
    /// </param>
    /// <param name="tokenCache">
    /// The cache to keep tokens in, or <see langword="null"/> for the SDK's
    /// shared in-memory cache.
    /// </param>
    private protected OcctooTokenCredential(string cacheKey, IFusionCache? tokenCache)
    {
        _cacheKey = $"occtoo:token:{cacheKey}";
        _cache = tokenCache ?? SharedInMemoryCache.Value;
    }

    /// <summary>
    /// How far ahead of expiry a token is replaced. Defaults to one minute.
    /// </summary>
    /// <remarks>
    /// Occtoo tokens live for 60 minutes and the token endpoint is rate-limited,
    /// so this is deliberately small: the goal is to avoid using a token that
    /// expires mid-flight, not to refresh often.
    /// </remarks>
    public TimeSpan RefreshSkew { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Supplies the current time. Overridden by tests; leave it alone otherwise.
    /// </summary>
    internal TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// The logger the credential's flow reports to. Defaults to a null logger;
    /// an <see cref="OcctooClient"/> adopts the credential into its
    /// <see cref="OcctooClientOptions.LoggerFactory"/> on construction.
    /// </summary>
    private protected ILogger Logger => _logger;

    /// <summary>
    /// The flow's low-cardinality name for telemetry —
    /// <c>client_credentials</c>, <c>device_code</c>, <c>delegate</c> —
    /// emitted as the <c>occtoo.credential.type</c> span attribute.
    /// </summary>
    private protected abstract string CredentialType { get; }

    /// <summary>
    /// Points the credential at a logger, once — the first client to adopt the
    /// credential wins, so a credential shared across clients logs consistently.
    /// </summary>
    internal void UseLogger(ILoggerFactory loggerFactory)
    {
        if (_logger == NullLogger.Instance)
            _logger = loggerFactory.CreateLogger(OcctooLogCategories.Authentication);
    }

    /// <inheritdoc />
    public async ValueTask<UnitResult<OcctooError>> Apply(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return UnitResult.Failure<OcctooError>(new ValidationError("A request is required."));

        var token = await GetToken(cancellationToken).ConfigureAwait(false);
        if (token.IsFailure)
            return UnitResult.Failure(token.Error);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value.Value);
        return UnitResult.Success<OcctooError>();
    }

    /// <summary>
    /// Returns a usable token, acquiring or refreshing one only when the cached
    /// token is missing or about to expire.
    /// </summary>
    /// <param name="cancellationToken">Cancels the acquisition.</param>
    /// <returns>A token valid at the time of the call, or the error that prevented acquiring one.</returns>
    public async ValueTask<Result<AccessToken, OcctooError>> GetToken(CancellationToken cancellationToken = default)
    {
        var cached = await _cache.TryGetAsync<AccessToken>(_cacheKey, token: cancellationToken).ConfigureAwait(false);
        if (cached.HasValue && !cached.Value.IsStale(TimeProvider.GetUtcNow(), RefreshSkew))
        {
            OcctooLog.TokenCacheHit(_logger, cached.Value.ExpiresOn);
            return cached.Value;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another request may have refreshed while this one waited.
            cached = await _cache.TryGetAsync<AccessToken>(_cacheKey, token: cancellationToken).ConfigureAwait(false);
            if (cached.HasValue && !cached.Value.IsStale(TimeProvider.GetUtcNow(), RefreshSkew))
            {
                OcctooLog.TokenCacheHit(_logger, cached.Value.ExpiresOn);
                return cached.Value;
            }

            OcctooLog.TokenRefreshNeeded(_logger, cached.HasValue ? "about to expire" : "missing");

            using var activity = OcctooTelemetry.Source.StartActivity("authenticate", ActivityKind.Client);
            activity?.SetTag("occtoo.credential.type", CredentialType);

            var expired = cached.HasValue ? Maybe.From(cached.Value) : Maybe<AccessToken>.None;

            Result<AccessToken, OcctooError> acquired;
            try
            {
                acquired = await AcquireToken(expired, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A flow implementation or user callback (FromDelegate,
                // promptUser) threw. The no-throw contract holds at this
                // boundary: the surprise becomes a result like everything else.
                acquired = new UnexpectedError($"The credential flow failed unexpectedly: {exception.Message}");
            }

            if (acquired.IsFailure)
            {
                OcctooTelemetry.Fail(activity, acquired.Error);
                OcctooLog.TokenAcquisitionFailed(_logger, acquired.Error);
                return acquired;
            }

            await _cache.SetAsync(
                _cacheKey,
                acquired.Value,
                new FusionCacheEntryOptions { Duration = CacheDuration(acquired.Value) },
                token: cancellationToken).ConfigureAwait(false);

            OcctooLog.TokenAcquired(_logger, acquired.Value.ExpiresOn);
            return acquired;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Discards the cached token so the next request acquires a fresh one.
    /// </summary>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <remarks>
    /// Called by the SDK when Occtoo rejects a token that had not yet expired,
    /// which happens when a credential is revoked or rotated mid-process.
    /// </remarks>
    public async ValueTask Invalidate(CancellationToken cancellationToken = default)
    {
        OcctooLog.TokenInvalidated(_logger);
        await _cache.RemoveAsync(_cacheKey, token: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Discards the cached token only if it is still the one that was rejected,
    /// so concurrent <c>401</c>s do not evict each other's freshly acquired
    /// replacements and cascade into repeated acquisitions.
    /// </summary>
    internal async ValueTask InvalidateIfCurrent(string rejectedToken, CancellationToken cancellationToken)
    {
        var cached = await _cache.TryGetAsync<AccessToken>(_cacheKey, token: cancellationToken).ConfigureAwait(false);
        if (!cached.HasValue || cached.Value.Value == rejectedToken)
            await Invalidate(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the flow that obtains a token.
    /// </summary>
    /// <param name="expired">
    /// The token being replaced, if any. Flows that can renew cheaply — a device
    /// code login holding a refresh token, for instance — use this instead of
    /// starting over.
    /// </param>
    /// <param name="cancellationToken">Cancels the acquisition.</param>
    /// <returns>The acquired token, or the error that prevented acquiring one.</returns>
    protected abstract ValueTask<Result<AccessToken, OcctooError>> AcquireToken(
        Maybe<AccessToken> expired,
        CancellationToken cancellationToken);

    private TimeSpan CacheDuration(AccessToken token)
    {
        // A token with no known expiry is re-requested daily rather than held
        // for ever: the flow that produced it can produce another, and a
        // bounded duration keeps abandoned entries from accumulating in a
        // shared cache.
        if (token.ExpiresOn == DateTimeOffset.MaxValue)
            return TimeSpan.FromDays(1);

        var lifetime = token.ExpiresOn - TimeProvider.GetUtcNow();
        return lifetime > TimeSpan.FromSeconds(1) ? lifetime : TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Removes this credential's cached token synchronously — for disposal
    /// paths of credentials whose cache entry no other instance can ever reach.
    /// </summary>
    private protected void RemoveCachedToken() => _cache.Remove(_cacheKey);

    /// <summary>
    /// Hashes a secret for use inside a cache key, so rotating the secret yields
    /// a different key without the key ever containing the secret.
    /// </summary>
    private protected static string HashSecret(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    /// <summary>
    /// Releases the resources held by the credential.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the resources held by the credential.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when called from <see cref="Dispose()"/>.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
            _gate.Dispose();

        _disposed = true;
    }
}
