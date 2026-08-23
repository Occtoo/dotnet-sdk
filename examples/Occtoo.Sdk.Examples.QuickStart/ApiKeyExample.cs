using Occtoo.Authentication;

namespace Occtoo.Sdk.Examples.QuickStart;

/// <summary>
/// An organization API key, sent as <c>x-api-key</c> — the least setup, no
/// token exchange at all. Note that typed ingest requires an application bearer
/// token with <c>write:sources</c>, so with this credential step 2 demonstrates
/// the typed error surface rather than a successful ingest.
/// </summary>
internal static class ApiKeyExample
{
    // ── Configuration ──────────────────────────────────────────────────────
    private const string OcctooApiKey = "<your-organization-api-key>";
    // ───────────────────────────────────────────────────────────────────────

    public static IOcctooCredential Create() =>
        OcctooCredential.ApiKey(ApiKey.From(OcctooApiKey));
}
