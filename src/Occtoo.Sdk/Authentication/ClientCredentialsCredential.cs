using CSharpFunctionalExtensions;
using Occtoo.Authentication.Internal;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Authentication;

/// <summary>
/// Authenticates a machine-to-machine application with the OAuth 2.0 client
/// credentials grant against the Occtoo authorization server.
/// </summary>
/// <remarks>
/// The right choice for a service, worker or scheduled job: no user is involved,
/// the secret can be rotated, and permissions are granted to the application. The
/// token is acquired on first use and reused until it is close to expiring.
/// </remarks>
internal sealed class ClientCredentialsCredential : OcctooTokenCredential
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly OcctooAuthorityOptions _authority;
    private readonly ClientSecret _clientSecret;

    internal ClientCredentialsCredential(
        OcctooAuthorityOptions authority,
        ClientSecret clientSecret,
        HttpClient? httpClient = null,
        IFusionCache? tokenCache = null)
        : base(CacheKey(authority, clientSecret), tokenCache)
    {
        _authority = authority;
        _clientSecret = clientSecret;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    private protected override string CredentialType => "client_credentials";

    /// <inheritdoc />
    protected override async ValueTask<Result<AccessToken, OcctooError>> AcquireToken(
        Maybe<AccessToken> expired,
        CancellationToken cancellationToken)
    {
        List<KeyValuePair<string, string>> parameters =
        [
            new("grant_type", "client_credentials"),
            new("client_id", _authority.ClientId.Value),
            new("client_secret", _clientSecret.Value),
            new("audience", _authority.Audience.Value),
        ];

        if (_authority.Scope is { Length: > 0 } scope)
            parameters.Add(new("scope", scope));

        var result = await OAuthTokenEndpoint
            .PostGrant(_httpClient, _authority.TokenEndpoint, parameters, cancellationToken)
            .ConfigureAwait(false);

        // A missing expires_in deserializes as 0; treating that as instant
        // expiry would re-run the grant on every request. Unknown lifetime
        // means "use until rejected" — the 401 retry recovers.
        return result.Map(token => token.ExpiresIn > 0
            ? new AccessToken(token.AccessToken!, TimeProvider.GetUtcNow().AddSeconds(token.ExpiresIn))
            : AccessToken.WithoutExpiry(token.AccessToken!));
    }

    private static string CacheKey(OcctooAuthorityOptions authority, ClientSecret secret) =>
        $"cc|{authority.Authority.Host}|{authority.ClientId.Value}|{authority.Audience.Value}|{authority.Scope}|{HashSecret(secret.Value)}";

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsHttpClient)
            _httpClient.Dispose();

        base.Dispose(disposing);
    }
}
