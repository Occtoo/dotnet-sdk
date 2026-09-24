using CSharpFunctionalExtensions;

namespace Occtoo.Http.Internal;

/// <summary>Shared page validation and mapping for the management lists.</summary>
internal static class Pages
{
    internal static UnitResult<OcctooError> Validate(PageRequest page) =>
        page.Limit is < 1 or > PageRequest.MaxLimit
            ? new ValidationError($"Limit must be between 1 and {PageRequest.MaxLimit}.")
            : UnitResult.Success<OcctooError>();

    // Management lists return no cursor once exhausted, so the cursor's
    // presence is the has-more signal.
    internal static Page<TModel> ToPage<TDto, TModel>(
        IReadOnlyList<TDto> items,
        string? after,
        long? totalCount,
        Func<TDto, TModel> map)
    {
        var next = after is { Length: > 0 } ? Maybe.From(PageCursor.From(after)) : Maybe<PageCursor>.None;
        return new(
            [.. OcctooTransport.Elements(items, "items").Select(map)],
            next,
            next.HasValue,
            totalCount.HasValue ? Maybe.From(totalCount.Value) : Maybe<long>.None);
    }
}
