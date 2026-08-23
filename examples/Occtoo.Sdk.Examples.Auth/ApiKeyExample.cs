using Occtoo.Authentication;

namespace Occtoo.Sdk.Examples.Auth;

/// <summary>
/// An organization API key, sent as <c>x-api-key</c> — the least setup, no
/// token exchange at all. Note that typed ingest and events require an
/// application bearer token with the matching scope, which an API key cannot
/// carry.
/// </summary>
internal static class ApiKeyExample
{
    // ── Configuration ──────────────────────────────────────────────────────
    private const string OcctooApiKey = "<your-organization-api-key>";
    // ───────────────────────────────────────────────────────────────────────

    public static IOcctooCredential Create() =>
        OcctooCredential.ApiKey(ApiKey.From(OcctooApiKey));
}
