using System.Runtime.CompilerServices;
using Occtoo.Assets;

namespace Occtoo.Sdk.Examples.Assets.Sync;

/// <summary>What the source system says happened to one of its files.</summary>
internal abstract record FileEvent(AssetKey Key);

/// <summary>A new file, with its metadata.</summary>
internal sealed record FileAdded(AssetKey Key, string Path, FileMetadata Metadata) : FileEvent(Key);

/// <summary>A new version of an existing file, with the metadata that goes with it.</summary>
internal sealed record FileUpdated(AssetKey Key, string Path, FileMetadata Metadata) : FileEvent(Key);

/// <summary>New metadata for an existing file; the file itself is unchanged.</summary>
internal sealed record FileMetadataUpdated(AssetKey Key, FileMetadata Metadata) : FileEvent(Key);

/// <summary>A file that's gone from the source system.</summary>
internal sealed record FileDeleted(AssetKey Key) : FileEvent(Key);

/// <summary>The metadata the source system keeps per file. A null is a value it doesn't have.</summary>
internal sealed record FileMetadata(string TitleEn, string? TitleSv, IReadOnlyList<string> Tags, int? WidthMm, bool Published);

internal static class SourceSystem
{
    private static readonly FileMetadata _chair = new("Dining chair", null, ["chair", "oak"], 450, Published: true);
    private static readonly FileMetadata _lamp = new("Desk lamp", null, ["lamp", "brass"], 200, Published: true);

    /// <summary>
    /// Stands in for the source system's event feed — in a real service, a
    /// Service Bus or NATS subscription, or a webhook. It plays one file
    /// lifecycle per round, one event per interval, and starts over, so the
    /// worker never runs out of work and nothing piles up in the data source.
    /// </summary>
    public static async IAsyncEnumerable<FileEvent> ReadEvents(
        string folder,
        TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        FileEvent[] round =
        [
            new FileAdded("chair", Path.Combine(folder, "chair-blue.svg"), _chair),
            new FileAdded("lamp", Path.Combine(folder, "lamp.svg"), _lamp),
            new FileMetadataUpdated("lamp", _lamp with { TitleSv = "Skrivbordslampa", WidthMm = null }),
            new FileUpdated("chair", Path.Combine(folder, "chair-green.svg"), _chair with { Tags = ["chair", "oak", "green"] }),
            new FileDeleted("lamp"),
            new FileDeleted("chair"),
        ];

        using var timer = new PeriodicTimer(interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var fileEvent in round)
            {
                yield return fileEvent;

                if (!await timer.WaitForNextTickAsync(cancellationToken))
                    yield break;
            }
        }
    }
}
