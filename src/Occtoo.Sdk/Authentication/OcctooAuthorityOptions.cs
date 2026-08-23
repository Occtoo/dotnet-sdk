namespace Occtoo.Authentication;

/// <summary>
/// Points a credential at the authorization server that issues Occtoo access
/// tokens, and identifies the application authenticating against it.
/// </summary>
/// <remarks>
/// The client id comes from the Occtoo application registered for your
/// integration; the audience is your tenant id (or an API version id for a
/// protected destination API). The authority rarely needs changing — override
/// it only when Occtoo directs you to a different environment.
/// </remarks>
public sealed record OcctooAuthorityOptions
{
    /// <summary>
    /// The default Occtoo authorization server, <c>https://auth.occtoo.com</c>.
    /// </summary>
    public static Uri DefaultAuthority { get; } = new("https://auth.occtoo.com");

    /// <summary>
    /// The authorization server, without a trailing path. Defaults to
    /// <see cref="DefaultAuthority"/>. The SDK appends <c>/oauth2/token</c> and
    /// <c>/oauth2/device/auth</c> to it.
    /// </summary>
    public Uri Authority { get; init; } = DefaultAuthority;

    /// <summary>
    /// The client id of the registered application: a machine-to-machine
    /// application for <see cref="OcctooCredential.ClientCredentials"/>, or a
    /// native/single-page application (a public client, no secret) for
    /// <see cref="OcctooCredential.DeviceCode"/>.
    /// </summary>
    public required ClientId ClientId { get; init; }

    /// <summary>
    /// The audience the issued token must be valid for — the tenant id for
    /// ingest and events, or an API version id for a protected destination API.
    /// Sent as the <c>audience</c> parameter.
    /// </summary>
    public required Audience Audience { get; init; }

    /// <summary>
    /// Scopes to request — see <see cref="OcctooScopes"/>. Empty by default,
    /// which grants every tenant API scope enabled for the application.
    /// </summary>
    /// <remarks>
    /// Interactive flows that should survive a restart need
    /// <see cref="OcctooScopes.Offline"/>, which is what makes the authorization
    /// server return a refresh token.
    /// </remarks>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    internal Uri TokenEndpoint => new(Authority, "/oauth2/token");

    internal Uri DeviceAuthorizationEndpoint => new(Authority, "/oauth2/device/auth");

    internal string Scope => string.Join(' ', Scopes);

    internal void Validate()
    {
        if (!Authority.IsAbsoluteUri)
            throw new InvalidOperationException($"{nameof(Authority)} must be an absolute URI.");
    }
}
