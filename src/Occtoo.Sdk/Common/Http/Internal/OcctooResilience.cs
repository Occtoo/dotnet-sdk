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
            ShouldHandle = arguments => ValueTask.FromResult(ShouldRetry(arguments.Outcome, arguments.Context)),
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

    /// <summary>
    /// Marks a non-GET request as safe to send twice — an upsert, say. Unmarked
    /// writes are never replayed after an ambiguous failure.
    /// </summary>
    internal static readonly HttpRequestOptionsKey<bool> Replayable = new("Occtoo.Replayable");

    // A 429 was refused before processing, so any request can be resent. Any
    // other transient failure may hide a committed write: replaying a create
    // would turn its success into a conflict and lose a one-time secret, so
    // only reads and requests marked Replayable are resent.
    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome, ResilienceContext context)
    {
        if (!HttpClientResiliencePredicates.IsTransient(outcome))
            return false;

        if (outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            return true;

        var request = context.GetRequestMessage() ?? outcome.Result?.RequestMessage;
        return request is null
               || request.Method == HttpMethod.Get
               || (request.Options.TryGetValue(Replayable, out var replayable) && replayable);
    }
}
