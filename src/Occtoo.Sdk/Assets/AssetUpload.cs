namespace Occtoo.Assets;

/// <summary>
/// An asset and its bytes — what <see cref="AssetsClient.Upload"/> takes.
/// </summary>
/// <param name="Key">The key within the data source.</param>
/// <param name="Filename">The name the file is stored under.</param>
/// <param name="Content">Where the bytes come from.</param>
public sealed record AssetUpload(AssetKey Key, AssetFilename Filename, AssetContent Content)
{
    /// <summary>The key and filename on their own, as the API calls take them.</summary>
    public Asset Asset => new(Key, Filename);

    /// <summary>
    /// A file on disk, stored under its own name.
    /// </summary>
    /// <param name="key">The key within the data source.</param>
    /// <param name="path">The file to upload.</param>
    public static AssetUpload FromFile(AssetKey key, string path) =>
        new(key, AssetFilename.FromPath(path), AssetContent.FromFile(path));

    /// <summary>
    /// A file on disk, stored under a name of your choosing.
    /// </summary>
    /// <param name="key">The key within the data source.</param>
    /// <param name="filename">The name the file is stored under.</param>
    /// <param name="path">The file to upload.</param>
    public static AssetUpload FromFile(AssetKey key, AssetFilename filename, string path) =>
        new(key, filename, AssetContent.FromFile(path));

    /// <summary>
    /// A buffer already in memory.
    /// </summary>
    /// <param name="key">The key within the data source.</param>
    /// <param name="filename">The name the file is stored under.</param>
    /// <param name="bytes">The content to upload.</param>
    public static AssetUpload FromBytes(AssetKey key, AssetFilename filename, ReadOnlyMemory<byte> bytes) =>
        new(key, filename, AssetContent.FromBytes(bytes));
}
