namespace Occtoo.Http.Internal;

/// <summary>
/// The waits the SDK schedules itself, outside the transport's resilience
/// pipeline: reconnecting the event stream and sending an asset again.
/// </summary>
internal static class Delays
{
    /// <summary>The delay, spread by up to 20% either way.</summary>
    internal static TimeSpan Jittered(TimeSpan delay) =>
        TimeSpan.FromTicks((long)(delay.Ticks * (0.8 + (Random.Shared.NextDouble() * 0.4))));

    internal static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
