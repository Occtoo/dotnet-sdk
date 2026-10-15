using Occtoo.Sources;

namespace Occtoo.Assets;

/// <summary>
/// An asset, its bytes and its property values — what
/// <see cref="AssetsClient.Upload"/> and <see cref="AssetsClient.Replace"/> take.
/// </summary>
/// <param name="Key">The key within the data source.</param>
/// <param name="Filename">The name the file is stored under.</param>
/// <param name="Content">Where the bytes come from.</param>
/// <example>
/// <code>
/// AssetUpload upload = AssetUpload.FromFile("chair_blue_01", "files/chair-blue.jpg")
///     .WithLocalizedText("title", "Blue chair", "en")
///     .WithText("sku", "CH-001")
///     .WithList("tags", "summer", "outdoor");
/// </code>
/// The <c>From*</c> methods return an <see cref="EntryPropertyBuilder{TResult}"/>,
/// which converts to an upload implicitly or through <see cref="EntryPropertyBuilder{TResult}.Build"/>.
/// </example>
public sealed record AssetUpload(AssetKey Key, AssetFilename Filename, AssetContent Content)
{
    /// <summary>The key and filename on their own, as the API calls take them.</summary>
    public Asset Asset => new(Key, Filename);

    /// <summary>
    /// The property values set on the asset's entry. Empty unless added with
    /// the <c>With*</c> methods on the builder the <c>From*</c> methods return.
    /// </summary>
    /// <remarks>
    /// Every property must already exist on the data source; create it with
    /// <see cref="SourcesClient.UpsertProperty"/> first. Each value must match
    /// the property's configured type, and a property with no type takes plain
    /// text (<c>WithText</c>). List items are joined with the property's
    /// delimiter, so an item can't contain it. Occtoo checks the values, as it
    /// does for typed ingest, and one bad value fails the whole call.
    /// <see cref="PropertyValue.Clear"/> with a language clears that language;
    /// without one it clears a non-localized property.
    /// </remarks>
    public IReadOnlyList<EntryProperty> Properties { get; init; } = [];

    /// <summary>
    /// A file on disk, stored under its own name.
    /// </summary>
    /// <param name="key">The key within the data source.</param>
    /// <param name="path">The file to upload.</param>
    /// <returns>A builder to add property values to.</returns>
    public static EntryPropertyBuilder<AssetUpload> FromFile(AssetKey key, string path) =>
        new(properties => new AssetUpload(key, AssetFilename.FromPath(path), AssetContent.FromFile(path))
        {
            Properties = properties,
        });

    /// <summary>
    /// A file on disk, stored under a name of your choosing.
    /// </summary>
    /// <param name="key">The key within the data source.</param>
    /// <param name="filename">The name the file is stored under.</param>
    /// <param name="path">The file to upload.</param>
    /// <returns>A builder to add property values to.</returns>
    public static EntryPropertyBuilder<AssetUpload> FromFile(AssetKey key, AssetFilename filename, string path) =>
        new(properties => new AssetUpload(key, filename, AssetContent.FromFile(path))
        {
            Properties = properties,
        });

    /// <summary>
    /// A buffer already in memory.
    /// </summary>
    /// <param name="key">The key within the data source.</param>
    /// <param name="filename">The name the file is stored under.</param>
    /// <param name="bytes">The content to upload.</param>
    /// <returns>A builder to add property values to.</returns>
    public static EntryPropertyBuilder<AssetUpload> FromBytes(AssetKey key, AssetFilename filename, ReadOnlyMemory<byte> bytes) =>
        new(properties => new AssetUpload(key, filename, AssetContent.FromBytes(bytes))
        {
            Properties = properties,
        });
}
