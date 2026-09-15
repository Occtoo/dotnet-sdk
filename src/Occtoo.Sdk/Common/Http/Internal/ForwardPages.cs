using CSharpFunctionalExtensions;

namespace Occtoo.Http.Internal;

/// <summary>Shared page validation and mapping for the forward-paginated lists.</summary>
internal static class ForwardPages
{
    internal static Maybe<OcctooError> Validate(PageRequest page) =>
        page.Limit is < 1 or > PageRequest.MaxLimit
            ? new ValidationError($"Limit must be between 1 and {PageRequest.MaxLimit}.")
            : Maybe<OcctooError>.None;

    internal static ForwardPage<TModel> ToPage<TDto, TModel>(
        IReadOnlyList<TDto> items,
        string? after,
        long? totalCount,
        Func<TDto, TModel> map) =>
        new(
            [.. items.Select(map)],
            after is { Length: > 0 } ? Maybe.From(PageCursor.From(after)) : Maybe<PageCursor>.None,
            totalCount.HasValue ? Maybe.From(totalCount.Value) : Maybe<long>.None);
}
