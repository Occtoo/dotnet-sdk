using CSharpFunctionalExtensions;
using Occtoo.Authentication.Internal;
using Occtoo.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Authentication;

/// <summary>
/// Authenticates a user with the OAuth 2.0 device authorization grant
/// (RFC 8628): the SDK shows a short code, the user approves it in a browser,
/// and the SDK polls until they do.
/// </summary>
/// <remarks>
/// <para>
/// Made for hosts that cannot receive a browser redirect — CLIs, containers,
/// SSH sessions, headless tooling — and equally usable from a desktop app. The
/// client is public, so no secret is involved. Create one with
/// <see cref="OcctooCredential.DeviceCode"/>.
/// </para>
/// <para>
/// Request the <see cref="OcctooScopes.Offline"/> scope and supply an
/// <see cref="IOcctooTokenCache"/> to make sign-in survive a restart; otherwise
/// the user is prompted again each time the process starts.
/// </para>
/// <para>
/// Acquisition blocks until the user approves, so the
/// <see cref="CancellationToken"/> passed to the first call needs a timeout the
/// user can live with — or none at all.
/// </para>
/// </remarks>
public sealed class DeviceCodeCredential : OcctooTokenCredential
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly OcctooAuthorityOptions _authority;
    private readonly Func<DeviceCodeInfo, CancellationToken, Task> _promptUser;
    private readonly IOcctooTokenCache? _tokenCache;
    private readonly string _refreshTokenKey;
    private string _refreshToken = "";

    internal DeviceCodeCredential(
        OcctooAuthorityOptions authority,
        Func<DeviceCodeInfo, CancellationToken, Task> promptUser,
        IOcctooTokenCache? tokenCache = null,
        HttpClient? httpClient = null,
        IFusionCache? accessTokenCache = null)
        : base($"device|{authority.Authority.Host}|{authority.ClientId.Value}|{authority.Audience.Value}|{authority.Scope}", accessTokenCache)
    {
        ArgumentNullException.ThrowIfNull(promptUser);

        _authority = authority;
        _promptUser = promptUser;
        _tokenCache = tokenCache;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _refreshTokenKey = $"{authority.Authority.Host}|{authority.ClientId.Value}";
    }

    /// <summary>
    /// Forgets the sign-in: drops the cached access and refresh tokens, and
    /// removes the refresh token from <see cref="IOcctooTokenCache"/>. The next
    /// request prompts the user again.
    /// </summary>
    /// <param name="cancellationToken">Cancels the removal.</param>
    public async ValueTask SignOut(CancellationToken cancellationToken = default)
    {
        await Invalidate(cancellationToken).ConfigureAwait(false);
        _refreshToken = "";
        if (_tokenCache is not null)
            await _tokenCache.ClearRefreshToken(_refreshTokenKey, cancellationToken).ConfigureAwait(false);
    }

    private protected override string CredentialType => "device_code";

    /// <inheritdoc />
    protected override async ValueTask<Result<AccessToken, OcctooError>> AcquireToken(
        Maybe<AccessToken> expired,
        CancellationToken cancellationToken)
    {
        // Renewing silently is always preferable to prompting again.
        var refreshToken = _refreshToken;
        if (refreshToken is { Length: 0 } && _tokenCache is not null)
        {
            refreshToken = await _tokenCache
                .GetRefreshToken(_refreshTokenKey, cancellationToken)
                .ConfigureAwait(false) ?? "";
        }

        if (refreshToken is { Length: > 0 })
        {
            var renewed = await TryRenew(refreshToken, cancellationToken).ConfigureAwait(false);
            if (renewed.IsSuccess)
            {
                OcctooLog.DeviceSignInRenewed(Logger);
                return renewed.Value;
            }

            // The refresh token was rejected — it is revoked or expired. Drop it
            // and fall through to an interactive sign-in.
            OcctooLog.DeviceRefreshTokenRejected(Logger);
            _refreshToken = "";
            if (_tokenCache is not null)
                await _tokenCache.ClearRefreshToken(_refreshTokenKey, cancellationToken).ConfigureAwait(false);
        }

        return await SignIn(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<AccessToken, OcctooError>> TryRenew(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> parameters =
        [
            new("grant_type", "refresh_token"),
            new("client_id", _authority.ClientId.Value),
            new("refresh_token", refreshToken),
        ];

        var result = await OAuthTokenEndpoint
            .PostGrant(_httpClient, _authority.TokenEndpoint, parameters, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
            return result.ConvertFailure<AccessToken>();

        return await Store(result.Value, refreshToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<AccessToken, OcctooError>> SignIn(CancellationToken cancellationToken)
    {
        var authorization = await RequestDeviceCode(cancellationToken).ConfigureAwait(false);
        if (authorization.IsFailure)
            return authorization.ConvertFailure<AccessToken>();

        var device = authorization.Value;
        var interval = TimeSpan.FromSeconds(device.Interval > 0 ? device.Interval : 5);
        var expiresOn = TimeProvider.GetUtcNow().AddSeconds(device.ExpiresIn > 0 ? device.ExpiresIn : 300);

        var info = new DeviceCodeInfo(
            device.UserCode!,
            new Uri(device.VerificationUri!),
            device.VerificationUriComplete is { Length: > 0 } complete
                ? Maybe.From(new Uri(complete))
                : Maybe<Uri>.None,
            expiresOn);

        await _promptUser(info, cancellationToken).ConfigureAwait(false);
        OcctooLog.DeviceSignInStarted(Logger);

        List<KeyValuePair<string, string>> parameters =
        [
            new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            new("client_id", _authority.ClientId.Value),
            new("device_code", device.DeviceCode!),
        ];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TimeProvider.GetUtcNow() >= expiresOn)
            {
                return new AuthenticationError(
                    "The device code expired before it was approved. Start the sign-in again.",
                    "expired_token");
            }

            await Task.Delay(interval, TimeProvider, cancellationToken).ConfigureAwait(false);

            var result = await OAuthTokenEndpoint
                .PostGrant(_httpClient, _authority.TokenEndpoint, parameters, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return await Store(result.Value, previousRefreshToken: "", cancellationToken)
                    .ConfigureAwait(false);
            }

            var errorCode = result.Error is AuthenticationError { ErrorCode: var code }
                ? code.GetValueOrDefault("")
                : "";

            switch (errorCode)
            {
                case "authorization_pending":
                    OcctooLog.DevicePollPending(Logger, interval);
                    continue;

                case "slow_down":
                    // RFC 8628: back off by 5 seconds and keep going.
                    interval += TimeSpan.FromSeconds(5);
                    continue;

                case "expired_token":
                    return new AuthenticationError(
                        "The device code expired before it was approved. Start the sign-in again.",
                        errorCode);

                case "access_denied":
                    return new AuthenticationError("The sign-in was denied.", errorCode);

                default:
                    return result.ConvertFailure<AccessToken>();
            }
        }
    }

    private async Task<Result<DeviceAuthorizationResponse, OcctooError>> RequestDeviceCode(
        CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> parameters =
        [
            new("client_id", _authority.ClientId.Value),
            new("audience", _authority.Audience.Value),
        ];

        if (_authority.Scope is { Length: > 0 } scope)
            parameters.Add(new("scope", scope));

        return await OAuthTokenEndpoint
            .PostDeviceAuthorization(
                _httpClient,
                _authority.DeviceAuthorizationEndpoint,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<AccessToken, OcctooError>> Store(
        TokenResponse token,
        string previousRefreshToken,
        CancellationToken cancellationToken)
    {
        // The authorization server may rotate the refresh token; keep the
        // previous one when it does not.
        var refreshToken = token.RefreshToken is { Length: > 0 } issued
            ? issued
            : previousRefreshToken;

        _refreshToken = refreshToken;

        if (refreshToken is { Length: > 0 } && _tokenCache is not null)
        {
            await _tokenCache
                .SetRefreshToken(_refreshTokenKey, refreshToken, cancellationToken)
                .ConfigureAwait(false);
        }

        // A missing expires_in (0) means unknown lifetime, not instant expiry.
        return token.ExpiresIn > 0
            ? new AccessToken(token.AccessToken!, TimeProvider.GetUtcNow().AddSeconds(token.ExpiresIn))
            : AccessToken.WithoutExpiry(token.AccessToken!);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsHttpClient)
            _httpClient.Dispose();

        base.Dispose(disposing);
    }
}
