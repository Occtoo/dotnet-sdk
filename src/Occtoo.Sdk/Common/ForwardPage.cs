using CSharpFunctionalExtensions;
using Vogen;

namespace Occtoo;

/// <summary>
/// An opaque position in a forward-paginated list — the <c>after</c> value a
/// page returns. Cursors are only meaningful together with the filters the
/// page was requested with: keep the same filters when following one.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct PageCursor
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator PageCursor(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("A page cursor must not be empty.")
            : Validation.Ok;
}

/// <summary>
/// One page of a forward-only list. <c>After</c> continues the list and is
/// absent on the last page; <c>TotalCount</c> is present only when the API
/// computed it.
/// </summary>
public sealed record ForwardPage<T>(IReadOnlyList<T> Items, Maybe<PageCursor> After, Maybe<long> TotalCount);

/// <summary>
/// How much of a forward-only list to read: at most <c>Limit</c> items
/// (1–200, default 50) strictly after <c>After</c>.
/// </summary>
public sealed record PageRequest
{
    /// <summary>The API's page-size ceiling.</summary>
    public const int MaxLimit = 200;

    /// <summary>The position to continue from; absent starts at the beginning.</summary>
    public Maybe<PageCursor> After { get; init; } = Maybe<PageCursor>.None;

    /// <summary>Maximum items per page, 1–200. Defaults to 50.</summary>
    public int Limit { get; init; } = 50;
}
