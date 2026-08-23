using CSharpFunctionalExtensions;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Authentication;

/// <summary>
/// Authenticates with a token the host already has, supplied by a callback.
/// </summary>
/// <remarks>
/// The escape hatch that keeps the SDK usable when Occtoo's own flows do not
/// apply: a token from Azure Key Vault or a managed identity, the caller's token
/// forwarded on-behalf-of by an ASP.NET Core app, or a token minted by
/// infrastructure the SDK knows nothing about. The callback's identity is
/// unknowable, so each instance caches under its own key and never shares
/// tokens with another instance.
/// </remarks>
internal sealed class DelegateTokenCredential : OcctooTokenCredential
{
    private readonly Func<CancellationToken, ValueTask<Result<AccessToken, OcctooError>>> _getToken;

    internal DelegateTokenCredential(
        Func<CancellationToken, ValueTask<Result<AccessToken, OcctooError>>> getToken,
        IFusionCache? tokenCache = null)
        : base($"delegate|{Guid.NewGuid():n}", tokenCache)
    {
        ArgumentNullException.ThrowIfNull(getToken);
        _getToken = getToken;
    }

    private protected override string CredentialType => "delegate";

    /// <inheritdoc />
    protected override ValueTask<Result<AccessToken, OcctooError>> AcquireToken(
        Maybe<AccessToken> expired,
        CancellationToken cancellationToken) => _getToken(cancellationToken);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // The per-instance cache key is unreachable by anyone else, so the
        // entry would otherwise linger in a shared cache until its duration
        // lapses — a leak for hosts that create delegate credentials freely.
        if (disposing)
            RemoveCachedToken();

        base.Dispose(disposing);
    }
}
