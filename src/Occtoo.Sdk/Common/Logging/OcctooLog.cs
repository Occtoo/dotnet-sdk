using Microsoft.Extensions.Logging;

namespace Occtoo.Logging;

/// <summary>
/// The logger categories the SDK emits under. All share the <c>Occtoo</c>
/// prefix, so one <c>"Occtoo"</c> entry in a host's logging configuration
/// controls the whole SDK; the transport's own logs live separately under
/// <c>System.Net.Http.HttpClient.Occtoo</c>.
/// </summary>
public static class OcctooLogCategories
{
    /// <summary>Token acquisition, renewal, and sign-in flows.</summary>
    public const string Authentication = "Occtoo.Authentication";

    /// <summary>The authentication handler in the HTTP pipeline.</summary>
    public const string Http = "Occtoo.Http";

    /// <summary>The Sources feature — typed ingest.</summary>
    public const string Sources = "Occtoo.Sources";

    /// <summary>The Events feature — pulling and streaming.</summary>
    public const string Events = "Occtoo.Events";
}

/// <summary>
/// The SDK's log events, source-generated so logging costs nothing when the
/// level is off. Event ids: 1xx authentication, 2xx http, 3xx sources.
/// </summary>
/// <remarks>
/// The levels form a deliberate ladder: Information marks the rare, meaningful
/// state changes (a token acquired, a batch accepted); Warning marks failures
/// and recoveries; Debug narrates the decisions ("why did the SDK call the
/// token endpoint again?"); Trace admits the per-request chatter. Turning
/// <c>"Occtoo": "Debug"</c> on in logging configuration is the intended way to
/// trace a misbehaving integration.
/// </remarks>
internal static partial class OcctooLog
{
    // ── Authentication (1xx) ───────────────────────────────────────────────

    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "Access token acquired; expires {ExpiresOn:u}")]
    internal static partial void TokenAcquired(ILogger logger, DateTimeOffset expiresOn);

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning,
        Message = "Access token acquisition failed: {Error}")]
    internal static partial void TokenAcquisitionFailed(ILogger logger, OcctooError error);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information,
        Message = "Cached access token invalidated; a fresh token will be acquired on the next request")]
    internal static partial void TokenInvalidated(ILogger logger);

    [LoggerMessage(EventId = 103, Level = LogLevel.Debug,
        Message = "Cached access token is {Reason}; acquiring a new one")]
    internal static partial void TokenRefreshNeeded(ILogger logger, string reason);

    [LoggerMessage(EventId = 104, Level = LogLevel.Trace,
        Message = "Using cached access token; expires {ExpiresOn:u}")]
    internal static partial void TokenCacheHit(ILogger logger, DateTimeOffset expiresOn);

    [LoggerMessage(EventId = 110, Level = LogLevel.Information,
        Message = "Device sign-in started; waiting for the user to approve")]
    internal static partial void DeviceSignInStarted(ILogger logger);

    [LoggerMessage(EventId = 113, Level = LogLevel.Debug,
        Message = "Device authorization still pending; polling again in {Interval}")]
    internal static partial void DevicePollPending(ILogger logger, TimeSpan interval);

    [LoggerMessage(EventId = 111, Level = LogLevel.Debug,
        Message = "Sign-in renewed silently from the stored refresh token")]
    internal static partial void DeviceSignInRenewed(ILogger logger);

    [LoggerMessage(EventId = 112, Level = LogLevel.Information,
        Message = "Stored refresh token was rejected; starting an interactive sign-in")]
    internal static partial void DeviceRefreshTokenRejected(ILogger logger);

    // ── Http (2xx) ─────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 200, Level = LogLevel.Warning,
        Message = "Occtoo rejected a token before its stated expiry — the credential was likely rotated or revoked; refreshing and retrying once")]
    internal static partial void RetryingRejectedToken(ILogger logger);

    [LoggerMessage(EventId = 201, Level = LogLevel.Warning,
        Message = "Transient failure (status {StatusCode}); retry {Attempt}/{MaxAttempts} in {Delay}")]
    internal static partial void RetryScheduled(
        ILogger logger, int attempt, int maxAttempts, TimeSpan delay, int statusCode);

    // ── Sources (3xx) ──────────────────────────────────────────────────────

    [LoggerMessage(EventId = 300, Level = LogLevel.Debug,
        Message = "Ingesting {EntryCount} entries into source '{SourceId}'")]
    internal static partial void Ingesting(ILogger logger, int entryCount, string sourceId);

    [LoggerMessage(EventId = 301, Level = LogLevel.Information,
        Message = "Ingest accepted: {EntryCount} entries into '{SourceId}', correlation {CorrelationId}")]
    internal static partial void IngestAccepted(ILogger logger, int entryCount, string sourceId, Guid correlationId);

    [LoggerMessage(EventId = 302, Level = LogLevel.Warning,
        Message = "Ingest into '{SourceId}' failed: {Error}")]
    internal static partial void IngestFailed(ILogger logger, string sourceId, OcctooError error);

    // ── Events (4xx) ───────────────────────────────────────────────────────

    [LoggerMessage(EventId = 400, Level = LogLevel.Debug,
        Message = "Pulled {Count} events (more retained: {HasMore})")]
    internal static partial void EventsPulled(ILogger logger, int count, bool hasMore);

    [LoggerMessage(EventId = 401, Level = LogLevel.Information,
        Message = "Event stream connected")]
    internal static partial void EventStreamConnected(ILogger logger);

    [LoggerMessage(EventId = 402, Level = LogLevel.Warning,
        Message = "Event stream disconnected; reconnecting in {Delay}")]
    internal static partial void EventStreamReconnecting(ILogger logger, TimeSpan delay);

    [LoggerMessage(EventId = 403, Level = LogLevel.Warning,
        Message = "Skipped an event that could not be parsed: {Reason}")]
    internal static partial void EventSkipped(ILogger logger, string reason);
}
