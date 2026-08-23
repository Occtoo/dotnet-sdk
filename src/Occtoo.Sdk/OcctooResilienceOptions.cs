using Polly;

namespace Occtoo;

/// <summary>
/// How the client retries transient failures — <c>429</c>, <c>5xx</c>,
/// <c>408</c>, and network errors — before a request surfaces as a
/// <see cref="TransientError"/>.
/// </summary>
/// <remarks>
/// <para>
/// Built on Polly via <c>Microsoft.Extensions.Http.Resilience</c>, and on by
/// default: Occtoo's APIs are rate limited, and a <c>Retry-After</c> header on
/// a <c>429</c> states how long to wait. The retry honors that header exactly
/// (capped by <see cref="MaxDelay"/>) and falls back to
/// <see cref="BackoffType"/> over <see cref="BaseDelay"/> when there is none.
/// </para>
/// <para>
/// A failure that still fails after the last attempt surfaces as the usual
/// <see cref="OcctooError"/> — a <see cref="RateLimitError"/> with the final
/// <c>Retry-After</c>, a <see cref="ServerError"/>, and so on — so consumer
/// error handling is unchanged; it just fires less often. Retries wait inside
/// the request, so <see cref="OcctooClientOptions.Timeout"/> bounds the whole
/// attempt sequence.
/// </para>
/// </remarks>
public sealed record OcctooResilienceOptions
{
    /// <summary>
    /// Whether the client retries at all. On by default; turn off to handle
    /// every transient failure yourself, or when layering your own resilience
    /// handler onto the pipeline.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// How many retries follow the initial attempt. Defaults to 3.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// The base delay the backoff grows from, when the response carries no
    /// <c>Retry-After</c>. Defaults to one second.
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How the delay grows across attempts. Defaults to
    /// <see cref="DelayBackoffType.Exponential"/>, with jitter.
    /// </summary>
    public DelayBackoffType BackoffType { get; init; } = DelayBackoffType.Exponential;

    /// <summary>
    /// Randomizes delays to avoid retry storms from many clients throttled at
    /// once. On by default.
    /// </summary>
    public bool UseJitter { get; init; } = true;

    /// <summary>
    /// The ceiling for any single wait, including one demanded by a
    /// <c>Retry-After</c> header. Defaults to two minutes — a server asking for
    /// more than that is better surfaced to the caller as a
    /// <see cref="RateLimitError"/> than silently slept through.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(2);

    internal void Validate()
    {
        if (MaxRetryAttempts < 0)
            throw new InvalidOperationException($"{nameof(MaxRetryAttempts)} must not be negative.");

        if (BaseDelay < TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(BaseDelay)} must not be negative.");

        if (MaxDelay < BaseDelay)
            throw new InvalidOperationException($"{nameof(MaxDelay)} must be at least {nameof(BaseDelay)}.");
    }
}
