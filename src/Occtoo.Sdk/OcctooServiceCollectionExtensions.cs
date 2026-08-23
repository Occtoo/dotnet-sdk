using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Occtoo.Http;
using Occtoo.Http.Internal;
using Occtoo.Logging;

namespace Occtoo.DependencyInjection;

/// <summary>
/// Registers an <see cref="OcctooClient"/> with the dependency injection
/// container.
/// </summary>
public static class OcctooServiceCollectionExtensions
{
    /// <summary>The name of the <see cref="IHttpClientFactory"/> client the SDK uses.</summary>
    public const string HttpClientName = "Occtoo";

    /// <summary>
    /// Registers a singleton <see cref="OcctooClient"/> backed by
    /// <see cref="IHttpClientFactory"/>, with authentication in the handler
    /// pipeline.
    /// </summary>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/> for the SDK's client, so the handler
    /// pipeline can be extended — resilience, logging, a proxy.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The configured options are incomplete, surfaced when the client is first
    /// resolved.
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services.AddOcctooClient(options => options with
    /// {
    ///     Credential = OcctooCredential.ClientCredentials(authority, clientSecret),
    /// });
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddOcctooClient(
        this IServiceCollection services,
        Func<OcctooClientOptions, OcctooClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Options are resolved once: a credential caches tokens, so every client
        // must share the same instance.
        services.AddSingleton(_ => BuildOptions(configure));

        var builder = RegisterHttpClient(
            services,
            HttpClientName,
            provider => provider.GetRequiredService<OcctooClientOptions>());

        services.AddSingleton(provider => CreateClient(
            provider,
            HttpClientName,
            provider.GetRequiredService<OcctooClientOptions>()));

        return builder;
    }

    /// <summary>
    /// Registers a keyed singleton <see cref="OcctooClient"/>, so an application
    /// can talk to several Occtoo environments or tenants side by side. Resolve
    /// it with <c>[FromKeyedServices(key)]</c> or
    /// <see cref="ServiceProviderKeyedServiceExtensions.GetRequiredKeyedService{T}(IServiceProvider, object?)"/>.
    /// </summary>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/> for this key's client, so the handler
    /// pipeline can be extended per key.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The configured options are incomplete, surfaced when the client is first
    /// resolved.
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services.AddKeyedOcctooClient("europe", options => options with
    /// {
    ///     Credential = OcctooCredential.ClientCredentials(europeAuthority, europeSecret),
    /// });
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddKeyedOcctooClient(
        this IServiceCollection services,
        object serviceKey,
        Func<OcctooClientOptions, OcctooClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);
        ArgumentNullException.ThrowIfNull(configure);

        // The name carries a per-registration unique suffix: two distinct keys
        // whose ToString() collide (two sentinel objects, same-named members of
        // different enums) must not merge into one cumulative pipeline — that
        // would chain both auth handlers and send the wrong tenant's token.
        var httpClientName = $"{HttpClientName}:{serviceKey}:{Guid.NewGuid():n}";

        services.AddKeyedSingleton(serviceKey, (_, _) => BuildOptions(configure));

        var builder = RegisterHttpClient(
            services,
            httpClientName,
            provider => provider.GetRequiredKeyedService<OcctooClientOptions>(serviceKey));

        services.AddKeyedSingleton(serviceKey, (provider, key) => CreateClient(
            provider,
            httpClientName,
            provider.GetRequiredKeyedService<OcctooClientOptions>(key)));

        return builder;
    }

    private static OcctooClientOptions BuildOptions(Func<OcctooClientOptions, OcctooClientOptions> configure)
    {
        var options = configure(EmptyOptions);
        options.Validate();
        return options;
    }

    private static IHttpClientBuilder RegisterHttpClient(
        IServiceCollection services,
        string httpClientName,
        Func<IServiceProvider, OcctooClientOptions> resolveOptions) =>
        services
            .AddHttpClient(httpClientName, (provider, httpClient) =>
            {
                var options = resolveOptions(provider);
                httpClient.BaseAddress = options.BaseAddress;
                // The HttpClient-level timeout stays infinite: it would sever
                // long-lived event streams. OcctooTransport enforces
                // options.Timeout per request.
                httpClient.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler(provider =>
            {
                var options = resolveOptions(provider);
                return new OcctooAuthenticationHandler(
                    options.Credential,
                    options.RetryOnUnauthorized,
                    ResolveLoggerFactory(provider, options).CreateLogger(OcctooLogCategories.Http));
            })
            // Added after (= inside) authentication, so retries reuse the
            // applied token and a 401 refresh happens outside the retry loop.
            .AddHttpMessageHandler(provider =>
            {
                var options = resolveOptions(provider);
                var logger = ResolveLoggerFactory(provider, options).CreateLogger(OcctooLogCategories.Http);
                return OcctooResilience.CreateHandler(options.Resilience, logger)
                    ?? (DelegatingHandler)new NoOpHandler();
            });

    private static OcctooClient CreateClient(
        IServiceProvider provider,
        string httpClientName,
        OcctooClientOptions options)
    {
        var httpClient = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(httpClientName);

        return new OcctooClient(httpClient, options with
        {
            LoggerFactory = ResolveLoggerFactory(provider, options),
        });
    }

    /// <summary>
    /// The host's logging is the default; options only override it when a
    /// factory was set explicitly.
    /// </summary>
    private static ILoggerFactory ResolveLoggerFactory(
        IServiceProvider provider,
        OcctooClientOptions options) =>
        options.LoggerFactory != NullLoggerFactory.Instance
            ? options.LoggerFactory
            : provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;

    /// <summary>
    /// Fills the pipeline slot when retries are disabled — IHttpClientFactory
    /// handler chains are fixed at registration time, so the choice has to
    /// resolve to something.
    /// </summary>
    private sealed class NoOpHandler : DelegatingHandler;

    /// <summary>
    /// A placeholder passed to the configuration callback so options can be
    /// written as <c>options with { ... }</c>. The required
    /// <see cref="OcctooClientOptions.Credential"/> must be supplied there;
    /// <see cref="OcctooClientOptions.Validate"/> catches it if it is not.
    /// </summary>
    private static OcctooClientOptions EmptyOptions => new()
    {
        Credential = null!,
    };
}
