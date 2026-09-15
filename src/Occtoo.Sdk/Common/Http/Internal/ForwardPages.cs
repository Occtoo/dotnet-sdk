using System.Runtime.CompilerServices;
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

    /// <summary>
    /// Folds a paged read into one enumeration: fetch a page, yield its items,
    /// follow <c>After</c> until it is absent. Failures throw
    /// <see cref="OcctooListException"/> — an enumerable has no failure track.
    /// </summary>
    internal static async IAsyncEnumerable<T> ReadAll<T>(
        Func<Maybe<PageCursor>, CancellationToken, Task<Result<ForwardPage<T>, OcctooError>>> fetch,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var after = Maybe<PageCursor>.None;

        while (true)
        {
            var page = await fetch(after, cancellationToken).ConfigureAwait(false);
            if (page.IsFailure)
                throw new OcctooListException(page.Error);

            foreach (var item in page.Value.Items)
                yield return item;

            if (page.Value.After.HasNoValue)
                yield break;

            after = page.Value.After;
        }
    }
}
