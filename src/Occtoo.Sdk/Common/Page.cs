using CSharpFunctionalExtensions;
using Vogen;

namespace Occtoo;

/// <summary>
/// An opaque position in a paginated read — the <c>after</c> value a page
/// returns. Its shape is the API's business, not the caller's: persist it,
/// pass it back, never parse it. A cursor is only meaningful together with
/// the query it was returned for, so keep the same filters when following
/// one.
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
/// One page of a cursor-paginated read. <c>Next</c> continues the read;
/// <c>HasMore</c> says whether anything is left to continue to. For event
/// pulls <c>Next</c> is also present on the last page — it is the checkpoint
/// to resume from later — while management lists return no cursor once
/// exhausted. <c>Total</c> is present only when the API computed it.
/// </summary>
public sealed record Page<T>(
    IReadOnlyList<T> Items,
    Maybe<PageCursor> Next,
    bool HasMore,
    Maybe<long> Total);

/// <summary>
/// How much of a paginated list to read: at most <c>Limit</c> items (1–200,
/// default 50) strictly after <c>After</c>.
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
