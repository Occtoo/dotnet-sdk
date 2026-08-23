using CSharpFunctionalExtensions;

namespace Occtoo.Events;

/// <summary>
/// A position in the retained event stream: the sequence, and when the
/// producer supplied it, the event's time.
/// </summary>
public sealed record EventStreamPosition(EventSequence Sequence, Maybe<DateTimeOffset> Time);

/// <summary>
/// The shape of the retained stream — or of one filtered view of it — without
/// any event payloads: where it starts, where it currently ends, and how many
/// events it holds.
/// </summary>
/// <remarks>
/// <c>After</c> is the pull cursor for the latest position: pass it as
/// <see cref="EventQuery.After"/> (or <see cref="EventStreamOptions.After"/>)
/// to consume only events that occur from now on. Comparing a checkpoint
/// against <c>Latest</c> measures consumer lag; comparing it against
/// <c>First</c> tells whether the position is still retained. When no retained
/// event matches, <c>First</c>, <c>Latest</c> and <c>After</c> are absent and
/// <c>Total</c> is <c>0</c>.
/// </remarks>
public sealed record EventStreamMetadata(
    Maybe<EventStreamPosition> First,
    Maybe<EventStreamPosition> Latest,
    Maybe<EventCursor> After,
    long Total);
