using CSharpFunctionalExtensions;
using Occtoo.Cards;
using Occtoo.Events;
using Occtoo.Sources;

namespace Occtoo.Destinations;

/// <summary>Whether a related card comes back as a list or as at most one.</summary>
public enum EndpointCardinality
{
    /// <summary>A list of related cards.</summary>
    Plural,

    /// <summary>At most one related card.</summary>
    Singular,
}

/// <summary>A card property offered as a facet: <c>Size</c> values (1–1000), titled per language.</summary>
public sealed record EndpointFacet(int Size, IReadOnlyDictionary<LanguageCode, string> Header);

/// <summary>A card property an endpoint returns, under <c>Alias</c> when set.</summary>
public sealed record EndpointProperty(CardPropertyId Id, Maybe<string> Alias, Maybe<EndpointFacet> Facet);

/// <summary>The order of related cards: a property of the related card.</summary>
public sealed record EndpointOrder(CardPropertyId By, SortDirection Direction);

/// <summary>Related cards an endpoint returns under <c>Alias</c>, shaped by their own content.</summary>
public sealed record EndpointRelation(
    RelationDefinitionId Id,
    string Alias,
    Maybe<EndpointCardinality> Cardinality,
    EndpointContent Content,
    Maybe<EndpointOrder> Order);

/// <summary>
/// What an endpoint returns: cards of one card definition, the card
/// properties picked, optionally narrowed to segments, with related cards
/// nested. Build it with <see cref="Of"/>.
/// </summary>
public sealed record EndpointContent(
    CardDefinitionId CardDefinitionId,
    IReadOnlyList<EndpointProperty> Properties,
    IReadOnlyList<SegmentId> IncludeSegments,
    IReadOnlyList<SegmentId> ExcludeSegments,
    IReadOnlyList<EndpointRelation> Relations)
{
    /// <summary>Content of the cards of a card definition.</summary>
    public static EndpointContent Of(CardDefinitionId cardDefinitionId) => new(cardDefinitionId, [], [], [], []);

    /// <summary>Returns a card property, named <paramref name="alias"/> in the published API when set.</summary>
    public EndpointContent Property(CardPropertyId property, Maybe<string> alias = default) =>
        this with { Properties = [.. Properties, new EndpointProperty(property, alias, Maybe<EndpointFacet>.None)] };

    /// <summary>Returns a card property and offers it as a facet of <paramref name="size"/> values.</summary>
    public EndpointContent Facet(
        CardPropertyId property,
        int size = 50,
        IReadOnlyDictionary<LanguageCode, string>? header = null,
        Maybe<string> alias = default) =>
        this with
        {
            Properties =
            [
                .. Properties,
                new EndpointProperty(property, alias, new EndpointFacet(
                    size is >= 1 and <= 1000 ? size : throw new ArgumentOutOfRangeException(nameof(size), "A facet returns 1 to 1000 values."),
                    header ?? new Dictionary<LanguageCode, string>())),
            ],
        };

    /// <summary>Only cards in at least one of these segments of the card definition.</summary>
    public EndpointContent InSegments(params IReadOnlyList<SegmentId> segments) =>
        this with { IncludeSegments = [.. IncludeSegments, .. segments] };

    /// <summary>No cards in any of these segments of the card definition.</summary>
    public EndpointContent NotInSegments(params IReadOnlyList<SegmentId> segments) =>
        this with { ExcludeSegments = [.. ExcludeSegments, .. segments] };

    /// <summary>Nests the cards related through a relation definition, shaped by <paramref name="content"/>.</summary>
    public EndpointContent Relation(
        RelationDefinitionId relation,
        string alias,
        EndpointCardinality cardinality,
        EndpointContent content,
        Maybe<EndpointOrder> order = default) =>
        this with
        {
            Relations =
            [
                .. Relations,
                new EndpointRelation(relation,
                    string.IsNullOrWhiteSpace(alias) ? throw new ArgumentException("A relation needs an alias.", nameof(alias)) : alias,
                    cardinality, content ?? throw new ArgumentNullException(nameof(content)), order),
            ],
        };
}
