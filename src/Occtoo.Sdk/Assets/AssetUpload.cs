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
/// var upload = AssetUpload.FromFile("chair_blue_01", "files/chair-blue.jpg")
///     .WithLocalizedText("title", "Blue chair", "en")
///     .WithText("sku", "CH-001")
///     .WithList("tags", "summer", "outdoor");
/// </code>
/// The <c>With*</c> methods are extensions in <c>Occtoo.Sources</c>; the file
/// needs to import that namespace.
/// </example>
public sealed record AssetUpload(AssetKey Key, AssetFilename Filename, AssetContent Content)
    : IEntryPropertyBuilder<AssetUpload>
{
    /// <summary>The key and filename on their own, as the API calls take them.</summary>
    public Asset Asset => new(Key, Filename);

    /// <summary>
    /// The property values set on the asset's entry. Empty unless added with
    /// <see cref="WithProperty"/> or the typed <c>With*</c> methods in
    /// <see cref="EntryPropertyBuilderExtensions"/>.
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

    /// <summary>Adds an already-built property value.</summary>
    /// <param name="value">The property value.</param>
    /// <returns>
    /// A copy of the upload with the value added. The upload it was called on
    /// is unchanged.
    /// </returns>
    public AssetUpload WithProperty(EntryProperty value) => this with { Properties = [.. Properties, value] };

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
