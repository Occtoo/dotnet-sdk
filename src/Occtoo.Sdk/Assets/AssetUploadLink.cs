namespace Occtoo.Assets;

/// <summary>
/// A signed, expiring URL to send one asset's bytes to.
/// </summary>
/// <remarks>
/// <para>
/// The URL points at Azure Blob Storage, not at Occtoo: it carries its own
/// authorization and must never be sent an Occtoo token.
/// <see cref="AssetsClient.Transfer"/> uses a separate, unauthenticated
/// <c>HttpClient</c> for exactly that reason.
/// </para>
/// <para>
/// A link lives one hour, and a transfer that outlives it fails — the upload is
/// a single request, so an expired link means starting that asset's bytes
/// again with a fresh one from
/// <see cref="AssetsClient.RefreshUploadLinks"/>.
/// </para>
/// </remarks>
/// <param name="Key">The asset the link is for.</param>
/// <param name="Filename">The name the file is stored under — the same one completion must use.</param>
/// <param name="Url">Where to send the bytes.</param>
/// <param name="ExpiresAt">When the link stops working.</param>
public sealed record AssetUploadLink(AssetKey Key, AssetFilename Filename, Uri Url, DateTimeOffset ExpiresAt)
{
    /// <summary>The asset this link belongs to, ready to complete with.</summary>
    public Asset Asset => new(Key, Filename);

    /// <summary>Whether the link has passed its expiry.</summary>
    /// <param name="timeProvider">The clock to read; the system clock by default.</param>
    public bool HasExpired(TimeProvider? timeProvider = null) =>
        (timeProvider ?? TimeProvider.System).GetUtcNow() >= ExpiresAt;
}
