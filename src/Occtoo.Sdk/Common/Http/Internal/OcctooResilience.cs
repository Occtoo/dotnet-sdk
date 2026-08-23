using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Occtoo.Logging;
using Polly;

namespace Occtoo.Http.Internal;

/// <summary>
/// Builds the retry handler both construction paths share, so the DI pipeline
/// and a hand-built client behave identically.
/// </summary>
internal static class OcctooResilience
{
    /// <summary>
    /// The handler implementing <paramref name="options"/>, or nothing when
    /// retries are switched off.
    /// </summary>
    internal static DelegatingHandler? CreateHandler(OcctooResilienceOptions options, ILogger logger)
    {
        if (!options.Enabled || options.MaxRetryAttempts == 0)
            return null;

        var retry = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = options.MaxRetryAttempts,
            Delay = options.BaseDelay,
            BackoffType = options.BackoffType,
            UseJitter = options.UseJitter,
            MaxDelay = options.MaxDelay,
            // The server's Retry-After is authoritative when present.
            ShouldRetryAfterHeader = true,
            OnRetry = arguments =>
            {
                OcctooLog.RetryScheduled(
                    logger,
                    arguments.AttemptNumber + 1,
                    options.MaxRetryAttempts,
                    arguments.RetryDelay,
                    arguments.Outcome.Result is { } response
                        ? (int)response.StatusCode
                        : 0);
                return default;
            },
        };

        return new ResilienceHandler(
            new ResiliencePipelineBuilder<HttpResponseMessage>().AddRetry(retry).Build());
    }
}
