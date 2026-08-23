using Occtoo.Authentication;

namespace Occtoo.Sdk.Examples.QuickStart;

/// <summary>
/// An interactive sign-in for a CLI, container or desktop app: the SDK shows a
/// short code, you approve it in a browser, and the SDK polls until you do. The
/// client is a native/SPA application — public, no secret involved.
/// </summary>
internal static class DeviceLoginExample
{
    // ── Configuration ──────────────────────────────────────────────────────
    private const string OcctooNativeClientId = "<your-native-application-client-id>";
    private const string OcctooTenantId = "<your-tenant-id>";
    // ───────────────────────────────────────────────────────────────────────

    // Offline makes the authorization server issue a refresh token. Pair it
    // with an IOcctooTokenCache (keychain, DPAPI, ...) to make the sign-in
    // survive a restart; without one it lasts as long as the process.
    //
    // OpenBrowser launches the default browser at the verification page with
    // the code pre-filled, and prints the instruction as a fallback for
    // headless hosts. Use DeviceCodePrompt.ToConsole to only print, or pass
    // your own callback to show the code in your own UI.
    public static IOcctooCredential Create() =>
        OcctooCredential.DeviceCode(
            new OcctooAuthorityOptions
            {
                ClientId = ClientId.From(OcctooNativeClientId),
                Audience = Audience.From(OcctooTenantId),
                Scopes = [OcctooScopes.Offline],
            },
            promptUser: DeviceCodePrompt.OpenBrowser);
}
