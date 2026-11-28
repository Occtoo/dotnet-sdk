using CSharpFunctionalExtensions;
using Occtoo.Events;
using Vogen;

namespace Occtoo.Cards;

/// <summary>Whether the links between cards are built.</summary>
public enum RelationDefinitionStatus
{
    /// <summary>The links are being built for the first time.</summary>
    Initializing,

    /// <summary>The links are being rebuilt after a match change.</summary>
    Updating,

    /// <summary>The links are built.</summary>
    Active,
}

/// <summary>What orders the target cards related to a source card.</summary>
public enum RelationOrderBasis
{
    /// <summary>A number on each target card.</summary>
    TargetProperty,

    /// <summary>The order of a list on the source card.</summary>
    SourceList,
}

/// <summary>A relation definition's name: not blank.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct RelationDefinitionName
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator RelationDefinitionName(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input) ? Validation.Invalid("A relation definition name must not be empty.") : Validation.Ok;
}

/// <summary>
/// The card property of each side whose values link the cards: a source card
/// is related to the target cards whose value matches its own. <c>id</c> names
/// the card id.
/// </summary>
public sealed record RelationMatch(CardPropertyId SourceProperty, CardPropertyId TargetProperty);

/// <summary>How the target cards related to a source card are ordered.</summary>
public sealed record RelationOrder(Maybe<RelationOrderBasis> Basis, CardPropertyId Property, Maybe<CardPropertyId> TargetMatchProperty)
{
    /// <summary>By a <c>Decimal</c> or <c>Integer</c> card property of the target card definition.</summary>
    public static RelationOrder ByTargetProperty(CardPropertyId property) =>
        new(RelationOrderBasis.TargetProperty, property, Maybe<CardPropertyId>.None);

    /// <summary>
    /// As the values of a <c>List</c> card property of the source card, each
    /// looked up in <paramref name="targetMatchProperty"/> of the target cards —
    /// the match's target property when left out.
    /// </summary>
    public static RelationOrder BySourceList(CardPropertyId listProperty, Maybe<CardPropertyId> targetMatchProperty = default) =>
        new(RelationOrderBasis.SourceList, listProperty, targetMatchProperty);
}

/// <summary>
/// Links the cards of a source and a target card definition. Segments and
/// destination endpoints follow relation definitions by id.
/// </summary>
public sealed record RelationDefinition(
    RelationDefinitionId Id,
    string Name,
    Maybe<string> Description,
    IReadOnlyList<string> Tags,
    CardDefinitionId SourceCardDefinitionId,
    CardDefinitionId TargetCardDefinitionId,
    RelationMatch Match,
    Maybe<string> SourceLabel,
    Maybe<string> TargetLabel,
    Maybe<RelationOrder> Order,
    Maybe<RelationDefinitionStatus> Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Starts a change; fields left alone stay as they are.</summary>
    public UpdateRelationDefinition Edit() => new(Id);
}

/// <summary>
/// A new relation definition between two active card definitions, which must
/// differ. Its links are built in the background: it is
/// <see cref="RelationDefinitionStatus.Initializing"/> until that finishes.
/// </summary>
public sealed record CreateRelationDefinition
{
    private CreateRelationDefinition(RelationDefinitionId id, RelationDefinitionName name, CardDefinitionId source, CardDefinitionId target, RelationMatch match)
    {
        Id = id;
        Name = name;
        SourceCardDefinitionId = source;
        TargetCardDefinitionId = target;
        Match = match;
    }

    internal RelationDefinitionId Id { get; }

    internal RelationDefinitionName Name { get; }

    internal CardDefinitionId SourceCardDefinitionId { get; }

    internal CardDefinitionId TargetCardDefinitionId { get; }

    internal RelationMatch Match { get; }

    internal Maybe<string> Description { get; private init; }

    internal Maybe<IReadOnlyList<string>> Tags { get; private init; }

    internal Maybe<string> SourceLabel { get; private init; }

    internal Maybe<string> TargetLabel { get; private init; }

    internal Maybe<RelationOrder> Order { get; private init; }

    /// <summary>
    /// Starts a relation definition. Its id can't match a card property id of
    /// either card definition, and can't change later.
    /// </summary>
    public static Source WithId(RelationDefinitionId id, RelationDefinitionName name) => new(id, name);

    /// <summary>A free-text note.</summary>
    public CreateRelationDefinition Described(string description) => this with { Description = description };

    /// <summary>Labels for grouping and filtering.</summary>
    public CreateRelationDefinition Tagged(params IReadOnlyList<string> tags) => this with { Tags = Maybe.From(tags) };

    /// <summary>The relation's label on each side, such as <c>Variants</c> and <c>Variant of</c>; up to 255 characters each.</summary>
    public CreateRelationDefinition Labelled(string sourceLabel, string targetLabel) =>
        this with { SourceLabel = sourceLabel, TargetLabel = targetLabel };

    /// <summary>How the related target cards are ordered; unordered otherwise.</summary>
    public CreateRelationDefinition OrderedBy(RelationOrder order) =>
        this with { Order = order ?? throw new ArgumentNullException(nameof(order)) };

    /// <summary>The source side of a new relation definition.</summary>
    public sealed class Source
    {
        private readonly RelationDefinitionId _id;
        private readonly RelationDefinitionName _name;

        internal Source(RelationDefinitionId id, RelationDefinitionName name)
        {
            _id = id;
            _name = name;
        }

        /// <summary>The source card definition and the card property whose value links its cards.</summary>
        public Target From(CardDefinitionId cardDefinitionId, CardPropertyId property) => new(_id, _name, cardDefinitionId, property);
    }

    /// <summary>The target side of a new relation definition.</summary>
    public sealed class Target
    {
        private readonly RelationDefinitionId _id;
        private readonly RelationDefinitionName _name;
        private readonly CardDefinitionId _source;
        private readonly CardPropertyId _sourceProperty;

        internal Target(RelationDefinitionId id, RelationDefinitionName name, CardDefinitionId source, CardPropertyId sourceProperty)
        {
            _id = id;
            _name = name;
            _source = source;
            _sourceProperty = sourceProperty;
        }

        /// <summary>
        /// The target card definition and the card property matched against the
        /// source's. Both must be indexed <c>Text</c> or <c>List</c> card
        /// properties, not both lists, or <c>id</c>.
        /// </summary>
        public CreateRelationDefinition To(CardDefinitionId cardDefinitionId, CardPropertyId property) =>
            new(_id, _name, _source, cardDefinitionId, new RelationMatch(_sourceProperty, property));
    }
}

/// <summary>Changes to a relation definition, from <see cref="RelationDefinition.Edit"/>.</summary>
public sealed record UpdateRelationDefinition
{
    internal UpdateRelationDefinition(RelationDefinitionId id) => Id = id;

    /// <summary>The relation definition to change.</summary>
    public RelationDefinitionId Id { get; }

    internal Maybe<RelationDefinitionName> Name { get; private init; }

    internal Maybe<string> Description { get; private init; }

    internal Maybe<IReadOnlyList<string>> Tags { get; private init; }

    internal Maybe<RelationMatch> Match { get; private init; }

    internal Maybe<string> SourceLabel { get; private init; }

    internal Maybe<string> TargetLabel { get; private init; }

    internal Maybe<RelationOrder> Order { get; private init; }

    /// <summary>A new name.</summary>
    public UpdateRelationDefinition WithName(RelationDefinitionName name) => this with { Name = name };

    /// <summary>A new free-text note; empty clears it.</summary>
    public UpdateRelationDefinition WithDescription(string description) => this with { Description = description ?? "" };

    /// <summary>New labels for grouping and filtering, replacing the old ones.</summary>
    public UpdateRelationDefinition WithTags(params IReadOnlyList<string> tags) => this with { Tags = Maybe.From(tags) };

    /// <summary>
    /// A new pair of card properties linking the cards; the links are built
    /// again. Only while the relation definition is
    /// <see cref="RelationDefinitionStatus.Active"/>, a <see cref="ConflictError"/> otherwise.
    /// </summary>
    public UpdateRelationDefinition WithMatch(CardPropertyId sourceProperty, CardPropertyId targetProperty) =>
        this with { Match = new RelationMatch(sourceProperty, targetProperty) };

    /// <summary>New labels for each side; empty clears one.</summary>
    public UpdateRelationDefinition Labelled(string sourceLabel, string targetLabel) =>
        this with { SourceLabel = sourceLabel ?? "", TargetLabel = targetLabel ?? "" };

    /// <summary>A new order of the related target cards.</summary>
    public UpdateRelationDefinition OrderedBy(RelationOrder order) =>
        this with { Order = order ?? throw new ArgumentNullException(nameof(order)) };
}

/// <summary>
/// Narrows and pages a relation definition listing, ordered by id; archived
/// ones are left out. Keep the same filters when following a cursor.
/// </summary>
public sealed record RelationDefinitionListQuery
{
    /// <summary>Case-insensitive substring of the name.</summary>
    public Maybe<string> Name { get; init; } = Maybe<string>.None;

    /// <summary>Only relation definitions from this card definition.</summary>
    public Maybe<CardDefinitionId> SourceCardDefinitionId { get; init; } = Maybe<CardDefinitionId>.None;

    /// <summary>Only relation definitions to this card definition.</summary>
    public Maybe<CardDefinitionId> TargetCardDefinitionId { get; init; } = Maybe<CardDefinitionId>.None;

    /// <summary>Only relation definitions in this state.</summary>
    public Maybe<RelationDefinitionStatus> Status { get; init; } = Maybe<RelationDefinitionStatus>.None;

    /// <summary>Lower bound on creation time.</summary>
    public Maybe<DateTimeOffset> CreatedFrom { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Upper bound on creation time.</summary>
    public Maybe<DateTimeOffset> CreatedTo { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Lower bound on last modification time.</summary>
    public Maybe<DateTimeOffset> UpdatedFrom { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Upper bound on last modification time.</summary>
    public Maybe<DateTimeOffset> UpdatedTo { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Where to read and how much.</summary>
    public PageRequest Page { get; init; } = new();
}
