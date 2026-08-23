# Authentication

Occtoo accepts two kinds of credential at its public API: an organization API
key in `x-api-key`, and a bearer token from the Occtoo authorization server
(`https://auth.occtoo.com`). The SDK covers both, plus the interactive flow a
CLI or desktop app needs, behind one abstraction.

Build a credential with `OcctooCredential` and hand it to the client:

```csharp
using var client = new OcctooClient(new OcctooClientOptions
{
    Credential = OcctooCredential.ApiKey(ApiKey.From(key)),
});
```

Credential inputs are value objects (`ApiKey`, `ClientId`, `ClientSecret`,
`Audience`, ...): they validate on construction, and the secret-shaped ones mask
their `ToString()` so an interpolated log line cannot leak them.

## Choosing one

| Your situation | Use | Why |
|---|---|---|
| A service, worker or scheduled job | `ClientCredentials` | Rotatable secret, permissions granted to the application, no user involved |
| A script or internal tool where a key is enough | `ApiKey` | No token exchange, no expiry, no round trip before the first call |
| A CLI, container or desktop app acting as a person | `DeviceCode` | The user approves in a browser; actions are attributable to them |
| A token minted by infrastructure the SDK does not model | `FromDelegate` | Key Vault, managed identity, or a token forwarded on-behalf-of |

For **typed ingest** (`client.Sources`), use `ClientCredentials` with your tenant
id as the audience and the `write:sources` scope.

### Organization API key

```csharp
Credential = OcctooCredential.ApiKey(ApiKey.From("..."))
```

Sent as `x-api-key` on every request. Nothing is cached because nothing is
exchanged. The trade-off is that the key's permissions are fixed and nothing it
does is attributable to a person — prefer `ClientCredentials` for anything
long-lived.

### Application (client credentials)

```csharp
Credential = OcctooCredential.ClientCredentials(
    new OcctooAuthorityOptions
    {
        ClientId = ClientId.From("..."),
        Audience = Audience.From(tenantId),
        Scopes = [OcctooScopes.WriteSources],
    },
    ClientSecret.From(clientSecret))
```

The OAuth 2.0 client credentials grant against the Occtoo authorization server.
The authority defaults to `https://auth.occtoo.com` and rarely needs changing —
override `Authority` only when Occtoo directs you to another environment.

The audience selects what the token is for: your tenant id for ingest and
events, or an API version id for a protected destination API. Scopes narrow what
the token can do (`OcctooScopes` has the known ones); omit them to receive every
tenant API scope enabled for the application.

The token is fetched on first use and reused for its full lifetime; concurrent
callers share a single in-flight acquisition rather than each triggering their
own.

Renewal: this grant issues no refresh token by design (RFC 6749 §4.4.3) —
holding the secret *is* the refresh capability. The SDK re-runs the grant on
its own, shortly before expiry and immediately after an early rejection.

### Device code (interactive)

```csharp
Credential = OcctooCredential.DeviceCode(
    new OcctooAuthorityOptions
    {
        ClientId = ClientId.From("..."),        // a native/SPA app — public, no secret
        Audience = Audience.From(tenantId),
        Scopes = ["openid", OcctooScopes.Offline],
    },
    promptUser: DeviceCodePrompt.OpenBrowser,
    tokenCache: myTokenCache)
```

RFC 8628: the SDK requests a code, invokes `promptUser` once, then polls until
the user approves — honouring the server's interval and backing off on
`slow_down`. Acquisition blocks for as long as the user takes, so pass a
`CancellationToken` with a timeout you are willing to wait, or none at all.

`promptUser` decides how the user gets to the verification page.
`DeviceCodePrompt.OpenBrowser` launches the default browser at the URL with the
code already embedded (printing the instruction too, as the fallback for
headless hosts); `DeviceCodePrompt.ToConsole` only prints. Or write your own —
a desktop app might show `info.UserCode` in its own UI.

Two things make sign-in survive a restart: the `OcctooScopes.Offline` scope
(what makes the server issue a refresh token) and an `IOcctooTokenCache` to
persist it — the SDK then renews silently and only prompts again when the
stored token is rejected. A refresh token grants access until revoked, so store
it accordingly (OS keychain, DPAPI, or a user-only file); the SDK deliberately
ships no default store rather than pick an insecure one. `SignOut()` clears
both the cached access token and the stored refresh token.

### A token minted elsewhere

```csharp
// Called on first use and again as the token nears expiry.
Credential = OcctooCredential.FromDelegate(async ct =>
{
    var result = await secretClient.GetTokenAsync(ct);
    return Result.Success<AccessToken, OcctooError>(
        new AccessToken(result.Token, result.ExpiresOn));
});
```

`FromDelegate` is the escape hatch for anything the SDK does not model — a
managed identity, an on-behalf-of token, your own infrastructure. It still gets
the SDK's caching and single-flight behaviour; for a token with no known
expiry, return `AccessToken.WithoutExpiry(value)`. There is deliberately no
fixed-token credential — whoever holds a raw token has a real flow available,
and a pasted token dies within the hour.

## Token caching

Token-based credentials keep their tokens in a
[FusionCache](https://github.com/ZiggyCreatures/FusionCache) — a shared
in-memory cache by default, so nothing needs configuring. Every
`OcctooCredential` factory accepts a `tokenCache` parameter; pass your own
`IFusionCache` to take control:

```csharp
var tokenCache = new FusionCache(new FusionCacheOptions { CacheName = "occtoo-tokens" });
// or one from DI, configured with a second-level provider:
services.AddFusionCache("occtoo-tokens")
    .WithSerializer(new FusionCacheSystemTextJsonSerializer())
    .WithDistributedCache(new RedisCache(redisOptions));

Credential = OcctooCredential.ClientCredentials(authority, secret, tokenCache: tokenCache);
```

With a distributed second level, tokens survive restarts and are shared across
replicas — which matters because Occtoo's token endpoints are rate-limited. Cache
keys include a hash of the secret (never the secret itself), so rotating a
credential naturally gets a fresh cache entry.

The cache stores access tokens only. Refresh tokens from interactive sign-ins go
through `IOcctooTokenCache`, which is a separate, deliberately security-focused
surface.

## What the SDK handles for you

**Token lifetime.** Acquired on first use, reused until `RefreshSkew` (one minute
by default) before expiry. Occtoo's token endpoints are rate-limited, so the SDK
refreshes as rarely as it safely can rather than as often as it could.

**Concurrency.** Twenty simultaneous requests on a cold client produce one token
request, not twenty.

**Revocation mid-process.** If Occtoo answers `401` with a token that has not yet
expired, the credential has been revoked or rotated. The SDK discards the token,
acquires a fresh one and retries the request once. Request bodies are buffered
before the first send so the retry can actually resend them. Turn this off with
`RetryOnUnauthorized = false`.

An API key is never retried this way — there is nothing to refresh, so a retry
would only repeat the same rejection.

## Failing early

```csharp
await client.Authenticate(cancellationToken)
    .Tap(token => logger.LogInformation("authenticated"))
    .TapError(error => logger.LogError("credential rejected: {Error}", error));
```

Establishes the credential without calling an API, so a wrong secret surfaces at
startup instead of inside the first import. Success carries `Maybe<AccessToken>`
(`None` for an API key, which has nothing to exchange); for an interactive
credential this is where the user is prompted, making it the natural place for
a CLI to sign in. Failure is an `OcctooError` — see [errors.md](errors.md).

## With dependency injection

```csharp
builder.Services.AddOcctooClient(options => options with
{
    Credential = OcctooCredential.ClientCredentials(authority, secret),
});
```

Registers a singleton `OcctooClient` over a named `IHttpClientFactory` client,
with authentication and the SDK's built-in retries
(see [errors.md](errors.md#built-in-retries)) in the handler pipeline. It
returns the `IHttpClientBuilder`, so logging, a proxy, or further resilience
can be layered on — if you add your own resilience handler, disable the SDK's
(`Resilience = new() { Enabled = false }`) so the two don't multiply retries:

```csharp
builder.Services
    .AddOcctooClient(options => options with
    {
        Credential = credential,
        Resilience = new OcctooResilienceOptions { Enabled = false },
    })
    .AddStandardResilienceHandler();
```

To talk to several Occtoo environments or tenants side by side, register keyed
clients:

```csharp
builder.Services.AddKeyedOcctooClient("europe", options => options with
{
    Credential = OcctooCredential.ClientCredentials(europeAuthority, europeSecret),
});

// resolve with [FromKeyedServices("europe")] OcctooClient client
```

The client and the credential are singletons per registration on purpose: a
credential instance *is* the token cache handle, so registering more than one
would multiply token requests.

## Observability

Token acquisition, renewals, and recoveries log under the
`Occtoo.Authentication` category, and every acquisition emits an
`authenticate` span when tracing is enabled — see
[observability.md](observability.md) for the full logging and OpenTelemetry
story.

## Extending it

Implement `IOcctooCredential` when a token has to be read per request from
ambient context; use `OcctooCredential.FromDelegate` for anything token-shaped,
which keeps the caching and single-flight behaviour. `OcctooAuthenticationHandler`
can be dropped into any `HttpClient` pipeline to authenticate requests the SDK
does not make itself — a failed credential surfaces there as
`OcctooCredentialException` carrying the `OcctooError`, because a
`DelegatingHandler` has no result channel.

## Not implemented

Authorization code with PKCE — deferred, not ruled out; see the open decisions
in [design-principles.md](design-principles.md#open-decisions).
