using System.Text.Json.Serialization;

namespace Occtoo.Authentication.Internal;

// Wire DTOs. Naming is handled by the context at the bottom (snake_case, the
// OAuth wire format), so no property here carries a naming attribute.

/// <summary>OAuth 2.0 token response.</summary>
internal sealed record TokenResponse
{
    public string? AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public int ExpiresIn { get; init; }

    public string? TokenType { get; init; }
}

/// <summary>OAuth 2.0 error response, shared by the token and device endpoints.</summary>
internal sealed record OAuthErrorResponse
{
    public string? Error { get; init; }

    public string? ErrorDescription { get; init; }
}

/// <summary>RFC 8628 device authorization response.</summary>
internal sealed record DeviceAuthorizationResponse
{
    public string? DeviceCode { get; init; }

    public string? UserCode { get; init; }

    public string? VerificationUri { get; init; }

    public string? VerificationUriComplete { get; init; }

    public int ExpiresIn { get; init; }

    public int Interval { get; init; }
}

/// <summary>
/// Source-generated serialization for the OAuth payloads, which are snake_case
/// on the wire (<c>access_token</c>, <c>expires_in</c>, ...).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(OAuthErrorResponse))]
[JsonSerializable(typeof(DeviceAuthorizationResponse))]
internal sealed partial class OAuthJsonContext : JsonSerializerContext;
