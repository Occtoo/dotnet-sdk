using CSharpFunctionalExtensions;

namespace Occtoo.Authentication;

/// <summary>
/// What the user must be shown to complete a device code sign-in.
/// </summary>
/// <param name="UserCode">The code the user types on the verification page.</param>
/// <param name="VerificationUri">The page the user opens.</param>
/// <param name="VerificationUriComplete">
/// The verification page with the code already embedded, when the authorization
/// server provides it. Prefer opening this — the user then only has to confirm.
/// </param>
/// <param name="ExpiresOn">When the code stops being accepted.</param>
public readonly record struct DeviceCodeInfo(
    string UserCode,
    Uri VerificationUri,
    Maybe<Uri> VerificationUriComplete,
    DateTimeOffset ExpiresOn)
{
    /// <summary>
    /// A ready-made instruction to show the user, for hosts that have nowhere
    /// better to put it than a line of text.
    /// </summary>
    public string Message =>
        $"To sign in, open {VerificationUriComplete.GetValueOrDefault(VerificationUri)} and enter the code {UserCode}.";
}
