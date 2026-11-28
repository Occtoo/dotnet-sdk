using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.Sources;

namespace Occtoo.Cards;

/// <summary>Which way a sort runs.</summary>
public enum SortDirection
{
    /// <summary>Smallest first.</summary>
    Ascending,

    /// <summary>Largest first.</summary>
    Descending,
}

/// <summary>Which value of a list property a sort compares.</summary>
public enum ListSortMode
{
    /// <summary>The lowest value.</summary>
    Min,

    /// <summary>The highest value.</summary>
    Max,
}

/// <summary>What facet values are sorted by.</summary>
public enum FacetOrder
{
    /// <summary>How many entries have the value; ties go by value, ascending.</summary>
    Count,

    /// <summary>The value itself.</summary>
    Value,
}

/// <summary>
/// The distinct values of one card property among the matching entries,
/// with how many entries have each. Build one with <see cref="On"/>.
/// </summary>
public sealed record CardFacet
{
    private CardFacet(CardPropertyId property) => Property = property;

    internal CardPropertyId Property { get; }

    internal Maybe<string> Contains { get; private init; }

    internal bool IgnoreCase { get; private init; }

    internal Maybe<(FacetOrder By, SortDirection Direction)> Sort { get; private init; }

    internal int First { get; private init; } = 50;

    internal Maybe<PageCursor> After { get; private init; }

    internal IReadOnlyList<string> Exclude { get; private init; } = [];

    /// <summary>A facet on a card property.</summary>
    public static CardFacet On(CardPropertyId property) => new(property);

    /// <summary>Only values containing the text.</summary>
    public CardFacet Containing(string text, bool ignoreCase = false) => this with { Contains = text, IgnoreCase = ignoreCase };

    /// <summary>The order of the values.</summary>
    public CardFacet SortedBy(FacetOrder by, SortDirection direction = SortDirection.Descending) => this with { Sort = (by, direction) };

    /// <summary>How many values to return; 50 when left out.</summary>
    public CardFacet Take(int count) =>
        this with { First = count > 0 ? count : throw new ArgumentOutOfRangeException(nameof(count), "Take at least one value.") };

    /// <summary>Continues after a facet's <see cref="CardFacetResult.Next"/>.</summary>
    public CardFacet ContinueAfter(PageCursor cursor) => this with { After = cursor };

    /// <summary>Leaves these values out.</summary>
    public CardFacet Excluding(params IReadOnlyList<string> values) => this with { Exclude = values };
}

/// <summary>
/// A card entry search: which entries, in which order, with which card
/// properties. Without <see cref="Select"/> each entry carries only its
/// metadata. Up to 100 properties, 10 sort keys and 10 facets.
/// </summary>
public sealed record CardSearch
{
    private CardSearch()
    {
    }

    internal Maybe<Where> Where { get; private init; }

    internal Maybe<LanguageCode> Language { get; private init; }

    internal IReadOnlyList<CardPropertyId> Properties { get; private init; } = [];

    internal IReadOnlyList<(CardPropertyId Property, SortDirection Direction, Maybe<ListSortMode> Mode)> Sort { get; private init; } = [];

    internal IReadOnlyList<CardFacet> Facets { get; private init; } = [];

    internal int Limit { get; private init; } = 25;

    internal Maybe<PageCursor> After { get; private init; }

    internal bool IncludeEntries { get; private init; } = true;

    internal bool IncludeTotal { get; private init; }

    internal bool IncludeSegments { get; private init; }

    /// <summary>Every entry.</summary>
    public static CardSearch All() => new();

    /// <summary>
    /// The entries the condition matches; localized card properties in it are
    /// read in <paramref name="language"/>.
    /// </summary>
    public static CardSearch Matching(Where where, Maybe<LanguageCode> language = default) =>
        new() { Where = where ?? throw new ArgumentNullException(nameof(where)), Language = language };

    /// <summary>The card properties each entry carries.</summary>
    public CardSearch Select(params IReadOnlyList<CardPropertyId> properties) => this with { Properties = properties };

    /// <summary>Adds a sort key; a list property compares its lowest or highest value by <paramref name="mode"/>.</summary>
    public CardSearch OrderBy(CardPropertyId property, SortDirection direction = SortDirection.Ascending, Maybe<ListSortMode> mode = default) =>
        this with { Sort = [.. Sort, (property, direction, mode)] };

    /// <summary>Adds a facet.</summary>
    public CardSearch FacetOn(CardFacet facet) =>
        this with { Facets = [.. Facets, facet ?? throw new ArgumentNullException(nameof(facet))] };

    /// <summary>Adds the segments each entry is a member of.</summary>
    public CardSearch WithSegments() => this with { IncludeSegments = true };

    /// <summary>Counts every matching entry into <see cref="Page{T}.Total"/>.</summary>
    public CardSearch WithTotal() => this with { IncludeTotal = true };

    /// <summary>Returns facets and counts only, no entries.</summary>
    public CardSearch FacetsOnly() => this with { IncludeEntries = false };

    /// <summary>How many entries a page holds: 1 to 100, 25 when left out.</summary>
    public CardSearch Take(int limit) =>
        this with { Limit = limit is >= 1 and <= 100 ? limit : throw new ArgumentOutOfRangeException(nameof(limit), "A page holds 1 to 100 entries.") };

    /// <summary>Continues after a page's <see cref="Page{T}.Next"/>; keep everything else the same.</summary>
    public CardSearch ContinueAfter(PageCursor cursor) => this with { After = cursor };
}

/// <summary>
/// One value of a card entry. A localized property yields one value per
/// language; a cleared value is <see cref="PropertyValue.Clear"/>.
/// </summary>
public sealed record CardEntryValue(
    CardPropertyId Id,
    PropertyValue Value,
    Maybe<SourcePropertyType> Type,
    Maybe<Delimiter> Delimiter,
    Maybe<DateTimeOffset> LastUpdated,
    Maybe<LanguageCode> Language);

/// <summary>
/// A card entry. <c>Segments</c> is present when the search asked for it
/// with <see cref="CardSearch.WithSegments"/>.
/// </summary>
public sealed record CardEntry(
    CardId Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CardEntryValue> Values,
    Maybe<IReadOnlyList<string>> Segments);

/// <summary>One distinct value of a facet and how many matching entries have it.</summary>
public sealed record CardFacetBucket(string Value, long Count);

/// <summary>A facet's values; <c>Next</c> continues them with <see cref="CardFacet.ContinueAfter"/>.</summary>
public sealed record CardFacetResult(CardPropertyId Property, IReadOnlyList<CardFacetBucket> Values, Maybe<PageCursor> Next, long TotalCount);

/// <summary>A page of card entries and the facets the search asked for.</summary>
public sealed record CardSearchResult(Page<CardEntry> Entries, IReadOnlyList<CardFacetResult> Facets);
