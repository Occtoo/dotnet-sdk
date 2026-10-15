using Occtoo.Sources;

namespace Occtoo.Assets;

/// <summary>
/// An asset and the property values its entry gets — what
/// <see cref="AssetsClient.Initialize"/> and <see cref="AssetsClient.Override"/> take.
/// </summary>
/// <param name="Asset">The asset's key and filename.</param>
/// <example>
/// <code>
/// await client.Assets.Initialize(
///     dataSourceId,
///     [
///         new AssetInitialization(new Asset("chair", "chair.jpg"))
///         {
///             Properties = [new EntryProperty("sku", PropertyValue.Text("CH-001"))],
///         },
///         new Asset("lamp", "lamp.jpg"),
///     ]);
/// </code>
/// </example>
public sealed record AssetInitialization(Asset Asset)
{
    /// <summary>
    /// The property values to set on the asset's entry. Empty by default. The
    /// rules are the ones on <see cref="AssetUpload.Properties"/>.
    /// </summary>
    public IReadOnlyList<EntryProperty> Properties { get; init; } = [];

    /// <summary>An asset with no property values.</summary>
    /// <param name="asset">The asset.</param>
    public static implicit operator AssetInitialization(Asset asset) => new(asset);
}