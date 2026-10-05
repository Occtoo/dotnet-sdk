namespace Occtoo.Sdk.Examples.Events.Batch;

/// <summary>
/// The <c>"Occtoo"</c> section of <c>appsettings.json</c>. Any standard
/// configuration source can override a value — in a real deployment, supply the
/// secret via the environment (<c>Occtoo__ClientSecret</c>) rather than a
/// committed file.
/// </summary>
public sealed record OcctooSettings
{
    /// <summary>The machine-to-machine application's client id.</summary>
    public string ClientId { get; init; } = "";

    /// <summary>The application's client secret.</summary>
    public string ClientSecret { get; init; } = "";

    /// <summary>The tenant whose events to consume — the token's audience.</summary>
    public string TenantId { get; init; } = "";

    /// <summary>The id (a GUID) of the durable consumer event destination to pull from.</summary>
    public string DestinationId { get; init; } = "";

    /// <summary>
    /// How many competing pull loops this process runs. Defaults to 2; run the
    /// process more than once and every loop in every process shares the work.
    /// </summary>
    public int Workers { get; init; } = 2;

    /// <summary>Maximum events per leased batch, 1–100. Defaults to 20.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>
    /// How long a batch stays leased to the worker processing it. Defaults to
    /// 60 seconds — keep it above the slowest batch you expect.
    /// </summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a loop waits after finding the consumer caught up. Defaults to 5 seconds.</summary>
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often a batch may be delivered before it is parked instead of
    /// failed again. Defaults to 5.
    /// </summary>
    public int MaxAttempts { get; init; } = 5;

    internal void Validate()
    {
        // Configuration mistakes should stop the host at startup, loudly —
        // this is the one place the SDK's conventions say throwing is right.
        string[] missing =
        [
            .. string.IsNullOrWhiteSpace(ClientId) ? new[] { nameof(ClientId) } : [],
            .. string.IsNullOrWhiteSpace(ClientSecret) ? new[] { nameof(ClientSecret) } : [],
            .. string.IsNullOrWhiteSpace(TenantId) ? new[] { nameof(TenantId) } : [],
            .. !Guid.TryParse(DestinationId, out var id) || id == Guid.Empty ? new[] { nameof(DestinationId) } : [],
        ];

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"The 'Occtoo' configuration section is missing: {string.Join(", ", missing)}. " +
                "See appsettings.json and the ClientSecret remarks in OcctooSettings.");
        }

        if (Workers < 1)
            throw new InvalidOperationException($"'{nameof(Workers)}' must be at least 1.");

        if (MaxAttempts < 1)
            throw new InvalidOperationException($"'{nameof(MaxAttempts)}' must be at least 1.");
    }
}
