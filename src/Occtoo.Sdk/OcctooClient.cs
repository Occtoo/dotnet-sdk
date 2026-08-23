using CSharpFunctionalExtensions;
using Occtoo.Authentication;
using Occtoo.Http;
using Occtoo.Http.Internal;
using Occtoo.Logging;
using Occtoo.Sources;

namespace Occtoo;

/// <summary>
/// The entry point to the Occtoo APIs.
/// </summary>
/// <remarks>
/// <para>
/// The client is thread-safe and meant to be long-lived: create one per Occtoo
/// environment and share it. Creating one per operation defeats token caching and
/// exhausts connections.
/// </para>
/// <para>
/// Expected failures are returned, not thrown — every operation returns
/// <c>Result&lt;T, OcctooError&gt;</c>. Exceptions are reserved for construction
/// mistakes (invalid options, dependency-injection wiring), cancellation, and
/// genuinely unexpected conditions.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// using var client = new OcctooClient(new OcctooClientOptions
/// {
///     Credential = OcctooCredential.ClientCredentials(authority, clientSecret),
/// });
///
/// await client.Sources.IngestEntries(sourceId, entries)
///     .Tap(receipt => logger.LogInformation("accepted {Id}", receipt.CorrelationId.Value))
///     .TapError(error => logger.LogWarning("rejected: {Error}", error));
/// </code>
/// </example>
public sealed class OcctooClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Creates a client that owns its <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="options">How to reach and authenticate against Occtoo.</param>
    /// <exception cref="InvalidOperationException"><paramref name="options"/> is incomplete.</exception>
    public OcctooClient(OcctooClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        Options = options;
        Credential = options.Credential;
        AdoptCredentialLogger(options);

        var httpLogger = options.LoggerFactory.CreateLogger(OcctooLogCategories.Http);

        // A bounded connection lifetime makes the long-lived client notice DNS
        // changes (failover, IP rotation) — the recovery IHttpClientFactory
        // gives the DI path through handler rotation.
        var transport = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        // Authentication outermost, retries inside it: a request that waits out
        // several backoffs still carries a token applied by the auth handler,
        // and a 401 refresh happens once, outside the retry loop.
        var resilience = OcctooResilience.CreateHandler(options.Resilience, httpLogger);
        if (resilience is not null)
            resilience.InnerHandler = transport;

        var authentication = new OcctooAuthenticationHandler(
            options.Credential,
            options.RetryOnUnauthorized,
            httpLogger)
        {
            InnerHandler = resilience ?? (HttpMessageHandler)transport,
        };

        _httpClient = new HttpClient(authentication)
        {
            BaseAddress = options.BaseAddress,
            Timeout = options.Timeout,
        };
        _ownsHttpClient = true;
        Sources = new SourcesClient(_httpClient, options.LoggerFactory.CreateLogger(OcctooLogCategories.Sources));
    }

    /// <summary>
    /// Creates a client over an <see cref="HttpClient"/> the caller owns —
    /// typically one from <see cref="IHttpClientFactory"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The client to send requests with. Its <see cref="HttpClient.BaseAddress"/>
    /// is set from <paramref name="options"/> when it has none, and its pipeline
    /// must already include an <see cref="OcctooAuthenticationHandler"/> — the
    /// SDK does not add one, because it cannot modify a handler chain it did not
    /// build. <see cref="Occtoo.DependencyInjection.OcctooServiceCollectionExtensions.AddOcctooClient"/>
    /// wires this up correctly.
    /// </param>
    /// <param name="options">How to reach and authenticate against Occtoo.</param>
    /// <exception cref="InvalidOperationException"><paramref name="options"/> is incomplete.</exception>
    public OcctooClient(HttpClient httpClient, OcctooClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        Options = options;
        Credential = options.Credential;
        AdoptCredentialLogger(options);

        httpClient.BaseAddress ??= options.BaseAddress;
        _httpClient = httpClient;
        _ownsHttpClient = false;
        Sources = new SourcesClient(_httpClient, options.LoggerFactory.CreateLogger(OcctooLogCategories.Sources));
    }

    private static void AdoptCredentialLogger(OcctooClientOptions options)
    {
        // The credential is built before any client exists, so the first client
        // to use it hands it the logger. Idempotent: later adoptions are no-ops.
        if (options.Credential is OcctooTokenCredential tokenCredential)
            tokenCredential.UseLogger(options.LoggerFactory);
    }

    /// <summary>The Occtoo API this client talks to.</summary>
    public Uri BaseAddress => _httpClient.BaseAddress ?? Options.BaseAddress;

    /// <summary>The credential this client authenticates with.</summary>
    public IOcctooCredential Credential { get; }

    /// <summary>The options the client was created with.</summary>
    public OcctooClientOptions Options { get; }

    /// <summary>
    /// The Sources feature — typed ingest of entries into sources.
    /// </summary>
    public SourcesClient Sources { get; }

    /// <summary>
    /// Establishes the credential without calling an API, so a misconfigured
    /// secret surfaces at startup rather than on the first real request.
    /// </summary>
    /// <param name="cancellationToken">Cancels the acquisition.</param>
    /// <returns>
    /// The token that was acquired — or nothing, for credentials that involve no
    /// token exchange (an API key, for instance, which cannot be checked without
    /// calling an API). Failure carries the <see cref="OcctooError"/> that
    /// explains the rejection.
    /// </returns>
    /// <remarks>
    /// Interactive credentials prompt the user here, which makes this the natural
    /// place for a CLI to sign in.
    /// </remarks>
    public async Task<Result<Maybe<AccessToken>, OcctooError>> Authenticate(
        CancellationToken cancellationToken = default)
    {
        if (Credential is not OcctooTokenCredential tokenCredential)
            return Maybe<AccessToken>.None;

        var token = await tokenCredential.GetToken(cancellationToken).ConfigureAwait(false);
        return token.Map(Maybe.From);
    }

    /// <summary>
    /// The authenticated transport the feature clients are built on.
    /// </summary>
    internal HttpClient HttpClient => _httpClient;

    /// <summary>
    /// Disposes the underlying <see cref="HttpClient"/>, if this client created it.
    /// </summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
