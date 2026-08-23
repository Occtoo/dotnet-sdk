namespace Occtoo.Authentication;

/// <summary>
/// A bearer token together with the moment it stops being usable.
/// </summary>
/// <param name="Value">The encoded token, sent as <c>Authorization: Bearer {Value}</c>.</param>
/// <param name="ExpiresOn">
/// When the token expires. <see cref="DateTimeOffset.MaxValue"/> means the token
/// never expires, or its lifetime is unknown to the SDK.
/// </param>
public readonly record struct AccessToken(string Value, DateTimeOffset ExpiresOn)
{
    /// <summary>
    /// A token that the SDK will never try to refresh on its own.
    /// </summary>
    /// <param name="value">The encoded token.</param>
    /// <returns>A token with no known expiry.</returns>
    public static AccessToken WithoutExpiry(string value) => new(value, DateTimeOffset.MaxValue);

    /// <summary>
    /// Whether the token is unusable at <paramref name="now"/>, allowing for
    /// <paramref name="refreshSkew"/> so a token is replaced slightly before it
    /// actually expires rather than mid-request.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="refreshSkew">How far ahead of expiry to consider the token stale.</param>
    /// <returns><see langword="true"/> when the token should be replaced.</returns>
    public bool IsStale(DateTimeOffset now, TimeSpan refreshSkew) =>
        ExpiresOn != DateTimeOffset.MaxValue && now + refreshSkew >= ExpiresOn;
}
