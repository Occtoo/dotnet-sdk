using System.Text.Json;
using CSharpFunctionalExtensions;
using Occtoo.Events;

namespace Occtoo.Sdk.Examples.EventConsumer;

/// <summary>
/// Persists the consumption position — the <c>after</c> cursor from the last
/// processed page — so a restart resumes exactly where the previous run
/// stopped. A file stands in for whatever a real service would use: a database
/// row, a blob, a Redis key. The contract is only "durably store one small
/// string, read it back on startup".
/// </summary>
/// <remarks>
/// The filter is stored alongside the cursor on purpose: a cursor is a
/// position in the tenant stream, not in a filtered view. Resuming an old
/// position with a different filter would silently skip every event the old
/// filter excluded — so when the filter changes, the checkpoint resets and
/// consumption starts over from the earliest retained event.
/// </remarks>
internal sealed class FileCheckpointStore(string path)
{
    private sealed record Checkpoint(string Cursor, string Filter);

    /// <summary>
    /// The stored cursor — or none on the first run, or when
    /// <paramref name="filter"/> no longer matches the one the cursor was
    /// earned under.
    /// </summary>
    public Maybe<EventCursor> Load(string filter)
    {
        if (!File.Exists(path))
            return Maybe<EventCursor>.None;

        var checkpoint = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path));
        return checkpoint is { Cursor.Length: > 0 } && checkpoint.Filter == filter
            ? Maybe.From(EventCursor.From(checkpoint.Cursor))
            : Maybe<EventCursor>.None;
    }

    /// <summary>
    /// Persists the cursor of a fully processed page. Call this only after
    /// every event on the page has been handled — the cursor points past the
    /// page, so saving earlier loses events on a crash.
    /// </summary>
    public void Save(EventCursor cursor, string filter) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new Checkpoint(cursor.Value, filter)));
}
