using CSharpFunctionalExtensions;

namespace Occtoo.Events;

/// <summary>
/// What to read from the event stream.
/// </summary>
public sealed record EventQuery
{
    /// <summary>
    /// Narrows the stream to matching events. Absent, every event type is
    /// delivered.
    /// </summary>
    public Maybe<EventFilter> Filter { get; init; } = Maybe<EventFilter>.None;

    /// <summary>
    /// The position to read strictly after — a cursor from a previous page, or
    /// a raw <see cref="EventSequence"/> for recovery. Absent, reading starts
    /// at the earliest retained event.
    /// </summary>
    public Maybe<EventCursor> After { get; init; } = Maybe<EventCursor>.None;

    /// <summary>
    /// Maximum events per page, between 1 and 1000. Defaults to 100.
    /// </summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Whether to compute the exact number of matching events after the
    /// cursor. Off by default — the count requires scanning all matches.
    /// </summary>
    public bool IncludeTotal { get; init; }
}

/// <summary>
/// How to subscribe to the live event stream.
/// </summary>
public sealed record EventStreamOptions
{
    /// <summary>
    /// Narrows the stream to matching events. Absent, every event type is
    /// delivered.
    /// </summary>
    public Maybe<EventFilter> Filter { get; init; } = Maybe<EventFilter>.None;

    /// <summary>
    /// The position to resume strictly after. Absent, the stream starts at the
    /// current tail and delivers only events that occur after subscribing.
    /// </summary>
    public Maybe<EventCursor> After { get; init; } = Maybe<EventCursor>.None;

    /// <summary>
    /// The first delay before reconnecting after the connection drops; doubles
    /// with jitter up to <see cref="MaxReconnectDelay"/> and resets once events
    /// flow again. Defaults to one second.
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The ceiling for the reconnect delay. Defaults to 30 seconds.</summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (InitialReconnectDelay <= TimeSpan.Zero)
            throw new InvalidOperationException($"{nameof(InitialReconnectDelay)} must be positive.");

        if (MaxReconnectDelay < InitialReconnectDelay)
            throw new InvalidOperationException(
                $"{nameof(MaxReconnectDelay)} must be at least {nameof(InitialReconnectDelay)}.");
    }
}

/// <summary>
/// Thrown by the event enumerables — <c>Stream</c> and <c>PullAll</c> — when
/// the stream fails for a reason reconnecting cannot fix, carrying the typed
/// <see cref="OcctooError"/>.
/// </summary>
/// <remarks>
/// This is the deliberate exception to the SDK's results-first contract: an
/// <c>IAsyncEnumerable</c> has no failure track, and transient failures are
/// already retried or reconnected internally — what reaches this exception is
/// a revoked credential, an invalid filter, or something equally permanent.
/// Caller cancellation still surfaces as <see cref="OperationCanceledException"/>.
/// </remarks>
public sealed class OcctooEventsException : Exception
{
    internal OcctooEventsException(OcctooError error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>Why the stream cannot continue.</summary>
    public OcctooError Error { get; }
}
