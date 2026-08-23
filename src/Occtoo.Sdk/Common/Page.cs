using CSharpFunctionalExtensions;
using Occtoo.Events;

namespace Occtoo;

/// <summary>
/// One page of a cursor-paginated result.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on this page, in stream order.</param>
/// <param name="Next">
/// The cursor to pass as <c>after</c> to fetch the next page. Persist it only
/// once this page has been processed successfully, and persist the filter it
/// was earned under alongside it — a cursor identifies a position in the
/// tenant stream, not in a filtered view.
/// </param>
/// <param name="HasMore">Whether more items were retained beyond this page.</param>
/// <param name="Total">
/// The exact number of matching items after the cursor, when the query asked
/// for it.
/// </param>
public sealed record Page<T>(
    IReadOnlyList<T> Items,
    Maybe<EventCursor> Next,
    bool HasMore,
    Maybe<long> Total);
