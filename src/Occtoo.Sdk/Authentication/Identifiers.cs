using Vogen;

namespace Occtoo.Authentication;

// The authentication identifiers, as value objects. Secrets deliberately
// override ToString so they cannot leak through logging or interpolation.

/// <summary>The OAuth client id of a registered Occtoo application.</summary>
[ValueObject<string>]
public readonly partial struct ClientId
{
    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("A client id must not be empty.")
            : Validation.Ok;
}

/// <summary>The OAuth client secret of a registered Occtoo application.</summary>
[ValueObject<string>]
public readonly partial struct ClientSecret
{
    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("A client secret must not be empty.")
            : Validation.Ok;

    /// <summary>A masked placeholder — the secret itself is never printed.</summary>
    public override string ToString() => "***";
}

/// <summary>
/// The audience an access token is requested for: the tenant id for ingest and
/// events, or an API version id for a protected destination API.
/// </summary>
[ValueObject<string>]
public readonly partial struct Audience
{
    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("An audience must not be empty.")
            : Validation.Ok;
}

/// <summary>An Occtoo organization API key, sent as <c>x-api-key</c>.</summary>
[ValueObject<string>]
public readonly partial struct ApiKey
{
    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("An API key must not be empty.")
            : Validation.Ok;

    /// <summary>A masked placeholder — the key itself is never printed.</summary>
    public override string ToString() => "***";
}

/// <summary>
/// The scopes Occtoo access tokens can be requested with. Omit the scope
/// entirely to receive every tenant API scope enabled for the application.
/// </summary>
public static class OcctooScopes
{
    /// <summary>Ingest data into sources the application is granted.</summary>
    public const string WriteSources = "write:sources";

    /// <summary>Every Events API transport.</summary>
    public const string ReadEvents = "read:events";

    /// <summary>Pull events and inspect stream metadata.</summary>
    public const string ReadEventsPull = "read:events:pull";

    /// <summary>Stream events over SSE.</summary>
    public const string ReadEventsSse = "read:events:sse";

    /// <summary>
    /// Makes interactive sign-ins return a refresh token, so they survive a
    /// process restart.
    /// </summary>
    public const string Offline = "offline";
}
