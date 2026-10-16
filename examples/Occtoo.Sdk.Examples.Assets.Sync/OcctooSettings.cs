namespace Occtoo.Sdk.Examples.Assets.Sync;

/// <summary>
/// The <c>"Occtoo"</c> section of <c>appsettings.json</c>. Pass the secret in
/// from outside: <c>Occtoo__ClientSecret</c> in the environment, or
/// <c>--Occtoo:ClientSecret</c> on the command line.
/// </summary>
public sealed record OcctooSettings
{
    /// <summary>The machine-to-machine application's client id.</summary>
    public string ClientId { get; init; } = "";

    /// <summary>The application's client secret.</summary>
    public string ClientSecret { get; init; } = "";

    /// <summary>The tenant to sync into — the token's audience.</summary>
    public string TenantId { get; init; } = "";

    /// <summary>The Media data source to sync into, created if it doesn't exist.</summary>
    public string SourceId { get; init; } = "sdkAssetsSync";

    /// <summary>The folder holding the source system's files, relative to the app.</summary>
    public string Folder { get; init; } = "files";

    /// <summary>How long the stand-in source system waits between events. Defaults to 30 seconds.</summary>
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
            .. string.IsNullOrWhiteSpace(Folder) ? new[] { nameof(Folder) } : [],
        ];

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"The 'Occtoo' configuration section is missing: {string.Join(", ", missing)}. " +
                "See appsettings.json; pass the secret as Occtoo__ClientSecret or --Occtoo:ClientSecret.");
        }
    }
}
