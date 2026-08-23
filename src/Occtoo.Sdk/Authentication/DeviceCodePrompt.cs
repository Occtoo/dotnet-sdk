using System.ComponentModel;
using System.Diagnostics;

namespace Occtoo.Authentication;

/// <summary>
/// Ready-made <c>promptUser</c> callbacks for
/// <see cref="OcctooCredential.DeviceCode"/>, so the common hosts do not have to
/// write their own.
/// </summary>
public static class DeviceCodePrompt
{
    /// <summary>
    /// Writes the verification instruction to the console — the right choice for
    /// SSH sessions, containers, and anywhere a browser cannot be launched.
    /// </summary>
    public static Func<DeviceCodeInfo, CancellationToken, Task> ToConsole { get; } =
        (info, _) =>
        {
            Console.WriteLine(info.Message);
            return Task.CompletedTask;
        };

    /// <summary>
    /// Opens the verification page in the user's default browser — preferring
    /// the URL with the code already embedded, so the user only has to confirm —
    /// and also writes the instruction to the console, in case the browser opens
    /// on another desktop or cannot be launched at all.
    /// </summary>
    /// <remarks>
    /// Launching a browser is inherently best-effort: on a headless host it
    /// fails quietly and the printed instruction is the fallback. The credential
    /// keeps polling either way, so sign-in completes as soon as the user
    /// approves — however they got to the page.
    /// </remarks>
    public static Func<DeviceCodeInfo, CancellationToken, Task> OpenBrowser { get; } =
        (info, _) =>
        {
            Console.WriteLine(info.Message);

            var url = info.VerificationUriComplete.GetValueOrDefault(info.VerificationUri);
            try
            {
                // UseShellExecute routes through the OS's default-browser
                // association: Windows directly, macOS via `open`, Linux via
                // `xdg-open`.
                Process.Start(new ProcessStartInfo
                {
                    FileName = url.ToString(),
                    UseShellExecute = true,
                });
            }
            catch (Win32Exception)
            {
                // No browser association — the printed message is the fallback.
            }
            catch (InvalidOperationException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }

            return Task.CompletedTask;
        };
}
