namespace Occtoo.Sdk.Examples.Sources.Ingest;

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

    /// <summary>The tenant to ingest into — the token's audience.</summary>
    public string TenantId { get; init; } = "";

    /// <summary>The source to ingest into.</summary>
    public string SourceId { get; init; } = "";

    /// <summary>How often the worker ingests. Defaults to 30 seconds.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        // Configuration mistakes should stop the host at startup, loudly —
        // this is the one place the SDK's conventions say throwing is right.
        string[] missing =
        [
            .. string.IsNullOrWhiteSpace(ClientId) ? new[] { nameof(ClientId) } : [],
            .. string.IsNullOrWhiteSpace(ClientSecret) ? new[] { nameof(ClientSecret) } : [],
            .. string.IsNullOrWhiteSpace(TenantId) ? new[] { nameof(TenantId) } : [],
            .. string.IsNullOrWhiteSpace(SourceId) ? new[] { nameof(SourceId) } : [],
        ];

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"The 'Occtoo' configuration section is missing: {string.Join(", ", missing)}. " +
                "See appsettings.json and the ClientSecret remarks in OcctooSettings.");
        }
    }
}
