namespace Occtoo.Authentication;

/// <summary>
/// Persists the refresh token from an interactive sign-in, so a user does not
/// have to authorize again every time the process restarts.
/// </summary>
/// <remarks>
/// <para>
/// The SDK caches access tokens in memory on its own; this exists only for the
/// long-lived refresh token. Without an implementation, a CLI or desktop app
/// prompts on every launch.
/// </para>
/// <para>
/// A refresh token grants access until it is revoked. Implementations must store
/// it somewhere appropriate for that — the OS keychain, DPAPI, or at minimum a
/// file with user-only permissions. Do not log it.
/// </para>
/// </remarks>
public interface IOcctooTokenCache
{
    /// <summary>Reads the stored refresh token, or <see langword="null"/> if there is none.</summary>
    /// <param name="key">Identifies the sign-in, so several accounts or environments can coexist.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The refresh token, or <see langword="null"/>.</returns>
    ValueTask<string?> GetRefreshToken(string key, CancellationToken cancellationToken);

    /// <summary>Stores a refresh token, replacing any previous one for the key.</summary>
    /// <param name="key">Identifies the sign-in.</param>
    /// <param name="refreshToken">The token to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    ValueTask SetRefreshToken(string key, string refreshToken, CancellationToken cancellationToken);

    /// <summary>Removes a stored refresh token — on sign-out, or once it is rejected.</summary>
    /// <param name="key">Identifies the sign-in.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    ValueTask ClearRefreshToken(string key, CancellationToken cancellationToken);
}
