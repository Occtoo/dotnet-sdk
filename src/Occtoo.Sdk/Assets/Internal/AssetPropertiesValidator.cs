using CSharpFunctionalExtensions;
using Occtoo.Sources;

namespace Occtoo.Assets.Internal;

// Validates the properties argument of Initialize and Override: each key
// belongs to an asset in the call, and no property list is null. Occtoo checks
// the values, as it does for typed ingest.
internal static class AssetPropertiesValidator
{
    internal static UnitResult<OcctooError> Validate(
        IReadOnlyCollection<Asset> assets,
        IReadOnlyDictionary<AssetKey, IReadOnlyList<EntryProperty>> properties)
    {
        // Failures use Occtoo's paths, which index the assets in the call's order.
        var indexes = new Dictionary<AssetKey, int>();
        foreach (var (asset, index) in assets.Select((asset, index) => (asset, index)))
            indexes.TryAdd(asset.Key, index);

        var failures = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (key, values) in properties)
        {
            if (!indexes.TryGetValue(key, out var index))
                failures[$"properties[{key.Value}]"] = ["No asset with this key exists in the call."];
            else if (values is null)
                failures[$"assets[{index}].properties"] = ["The property list must not be null."];
        }

        return failures.Count == 0
            ? UnitResult.Success<OcctooError>()
            : new ValidationError("Some asset properties are invalid; see Failures.", failures);
    }
}
