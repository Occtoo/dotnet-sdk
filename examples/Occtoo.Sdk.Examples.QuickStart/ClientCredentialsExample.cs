using Occtoo.Authentication;

namespace Occtoo.Sdk.Examples.QuickStart;

/// <summary>
/// A machine-to-machine application — the default for services, workers and
/// scheduled jobs. The secret is rotatable and permissions are granted to the
/// application itself.
/// </summary>
internal static class ClientCredentialsExample
{
    // ── Configuration ──────────────────────────────────────────────────────
    private const string OcctooClientId = "<your-application-client-id>";
    private const string OcctooClientSecret = "<your-application-client-secret>";
    private const string OcctooTenantId = "<your-tenant-id>";
    // ───────────────────────────────────────────────────────────────────────

    public static IOcctooCredential Create() =>
        OcctooCredential.ClientCredentials(
            new OcctooAuthorityOptions
            {
                ClientId = ClientId.From(OcctooClientId),
                Audience = Audience.From(OcctooTenantId),
                Scopes = [OcctooScopes.WriteSources],   // typed ingest needs write:sources
            },
            ClientSecret.From(OcctooClientSecret));
}
