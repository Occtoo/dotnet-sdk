using CSharpFunctionalExtensions;

namespace Occtoo.Assets;

/// <summary>
/// One asset, as the API identifies it: the key it is stored under and the
/// filename its blob takes.
/// </summary>
/// <remarks>
/// Every call in a single asset's life — initialize, complete, a refreshed
/// upload link — takes the pair, so the filename cannot drift between them.
/// </remarks>
/// <param name="Key">The key within the data source.</param>
/// <param name="Filename">The name the file is stored under.</param>
public sealed record Asset(AssetKey Key, AssetFilename Filename)
{
    /// <summary>
    /// An asset whose filename is the last segment of <paramref name="path"/>.
    /// </summary>
    /// <param name="key">The key within the data source.</param>
    /// <param name="path">A file path; only its filename is used.</param>
    public static Asset FromPath(AssetKey key, string path) => new(key, AssetFilename.FromPath(path));
}

/// <summary>
/// The per-key outcome of a batch call: what came back, and why the rest did
/// not.
/// </summary>
/// <remarks>
/// Occtoo processes these batches per asset, so a call can succeed while
/// individual keys are refused. The call itself is the
/// <c>Result&lt;T, OcctooError&gt;</c>; this is what a successful call carries.
/// </remarks>
/// <typeparam name="T">What a key that succeeded produced.</typeparam>
/// <param name="Succeeded">The keys that went through, with what came back for each.</param>
/// <param name="Failed">The keys Occtoo refused, with its reason for each.</param>
public sealed record AssetBatch<T>(
    IReadOnlyDictionary<AssetKey, T> Succeeded,
    IReadOnlyDictionary<AssetKey, string> Failed)
    where T : notnull
{
    /// <summary>Whether every key in the batch succeeded.</summary>
    public bool AllSucceeded => Failed.Count == 0;

    /// <summary>What came back for one key, or nothing if it was refused.</summary>
    /// <param name="key">The key to look up.</param>
    public Maybe<T> Find(AssetKey key) =>
        Succeeded.TryGetValue(key, out var value) ? Maybe.From(value) : Maybe<T>.None;
}
