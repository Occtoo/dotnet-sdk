using CSharpFunctionalExtensions;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Authentication;

/// <summary>
/// Builds the credential an <see cref="OcctooClient"/> authenticates with.
/// </summary>
/// <remarks>
/// <para>Which one to reach for:</para>
/// <list type="table">
///   <listheader><term>Situation</term><description>Credential</description></listheader>
///   <item>
///     <term>A service, worker or scheduled job</term>
///     <description><see cref="ClientCredentials"/> — rotatable secret, permissions granted to the application</description>
///   </item>
///   <item>
///     <term>A script or internal tool where a key is enough</term>
///     <description><see cref="ApiKey"/> — no token exchange, no expiry</description>
///   </item>
///   <item>
///     <term>A CLI, container or desktop app acting as a person</term>
///     <description><see cref="DeviceCode"/> — the user approves in a browser</description>
///   </item>
///   <item>
///     <term>A token that already exists elsewhere</term>
///     <description><see cref="FromDelegate"/></description>
///   </item>
/// </list>
/// <para>
/// Token-based credentials cache their tokens in an in-memory FusionCache by
/// default. Pass a <c>tokenCache</c> to any factory to use your own
/// <see cref="IFusionCache"/> — with a second-level provider (Redis, for
/// instance) tokens survive restarts and are shared across processes.
/// </para>
/// </remarks>
public static class OcctooCredential
{
    /// <summary>
    /// An Occtoo organization API key, sent as <c>x-api-key</c>.
    /// </summary>
    /// <param name="apiKey">The key, as issued by Occtoo.</param>
    /// <returns>The credential.</returns>
    public static IOcctooCredential ApiKey(ApiKey apiKey) => new ApiKeyCredential(apiKey);

    /// <summary>
    /// A machine-to-machine application, via the OAuth 2.0 client credentials
    /// grant against the Occtoo authorization server.
    /// </summary>
    /// <param name="authority">The application and authorization server to authenticate against.</param>
    /// <param name="clientSecret">The application's client secret.</param>
    /// <param name="httpClient">Optional client used to reach the token endpoint.</param>
    /// <param name="tokenCache">Optional cache for the issued tokens; in-memory when omitted.</param>
    /// <returns>The credential.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="authority"/> is incomplete.</exception>
    public static OcctooTokenCredential ClientCredentials(
        OcctooAuthorityOptions authority,
        ClientSecret clientSecret,
        HttpClient? httpClient = null,
        IFusionCache? tokenCache = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        authority.Validate();
        return new ClientCredentialsCredential(authority, clientSecret, httpClient, tokenCache);
    }

    /// <summary>
    /// An interactive user sign-in, via the OAuth 2.0 device authorization grant.
    /// </summary>
    /// <param name="authority">
    /// The application and authorization server to authenticate against. The
    /// application must be registered as a native or single-page application,
    /// and include <see cref="OcctooScopes.Offline"/> in
    /// <see cref="OcctooAuthorityOptions.Scopes"/> if sign-in should be
    /// remembered.
    /// </param>
    /// <param name="promptUser">
    /// Shows the code and verification URL to the user. Called once per sign-in,
    /// before polling starts. A console app can write
    /// <see cref="DeviceCodeInfo.Message"/>; a desktop app can open
    /// <see cref="DeviceCodeInfo.VerificationUriComplete"/> in a browser.
    /// </param>
    /// <param name="tokenCache">
    /// Where to persist the refresh token. Optional; without it, sign-in lasts
    /// only as long as the process.
    /// </param>
    /// <param name="httpClient">Optional client used to reach the authorization server.</param>
    /// <param name="accessTokenCache">Optional cache for the issued access tokens; in-memory when omitted.</param>
    /// <returns>The credential. Keep the reference to call <see cref="DeviceCodeCredential.SignOut"/>.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="authority"/> is incomplete.</exception>
    public static DeviceCodeCredential DeviceCode(
        OcctooAuthorityOptions authority,
        Func<DeviceCodeInfo, CancellationToken, Task> promptUser,
        IOcctooTokenCache? tokenCache = null,
        HttpClient? httpClient = null,
        IFusionCache? accessTokenCache = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        authority.Validate();
        return new DeviceCodeCredential(authority, promptUser, tokenCache, httpClient, accessTokenCache);
    }

    /// <summary>
    /// A token supplied by a callback, re-invoked as the token nears expiry —
    /// the escape hatch for tokens minted by infrastructure the SDK does not
    /// model, such as a secret store or a token forwarded on-behalf-of.
    /// </summary>
    /// <param name="getToken">
    /// Returns a token and its expiry, or the error that prevented obtaining
    /// one. The SDK guarantees only one call is in flight at a time.
    /// </param>
    /// <param name="tokenCache">Optional cache for the returned tokens; in-memory when omitted.</param>
    /// <returns>The credential.</returns>
    public static OcctooTokenCredential FromDelegate(
        Func<CancellationToken, ValueTask<Result<AccessToken, OcctooError>>> getToken,
        IFusionCache? tokenCache = null) =>
        new DelegateTokenCredential(getToken, tokenCache);
}
