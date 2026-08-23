using System.Diagnostics;

namespace Occtoo.Telemetry;

/// <summary>
/// The SDK's OpenTelemetry instrumentation, following the .NET convention: one
/// <see cref="ActivitySource"/>, dormant until a listener subscribes, so tracing
/// is a pure opt-in with zero cost otherwise.
/// </summary>
/// <remarks>
/// <para>Enable it the standard way:</para>
/// <code>
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(tracing => tracing.AddSource(OcctooTelemetry.ActivitySourceName));
/// </code>
/// <para>
/// The SDK emits logical operation spans — <c>ingest {source}</c> around a
/// typed ingest, <c>authenticate</c> around a token acquisition — so a trace
/// stays meaningful when <c>HttpClient</c> instrumentation is suppressed, and
/// gains the wire-level child spans when it is not. Attributes use the
/// <c>occtoo.*</c> namespace plus the OpenTelemetry-standard <c>error.type</c>;
/// failed operations set the span status to <see cref="ActivityStatusCode.Error"/>.
/// </para>
/// </remarks>
public static class OcctooTelemetry
{
    /// <summary>
    /// The name to pass to <c>AddSource</c>: <c>Occtoo.Sdk</c>.
    /// </summary>
    public const string ActivitySourceName = "Occtoo.Sdk";

    internal static readonly ActivitySource Source = new(
        ActivitySourceName,
        typeof(OcctooTelemetry).Assembly.GetName().Version?.ToString() ?? "");

    /// <summary>
    /// Marks <paramref name="activity"/> failed the OpenTelemetry way: error
    /// status with the message, and the standard <c>error.type</c> attribute
    /// carrying the low-cardinality error kind.
    /// </summary>
    internal static void Fail(Activity? activity, OcctooError error)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, error.Message);
        activity.SetTag("error.type", error.GetType().Name);
    }
}
