using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.ManagedTags;
using Occtoo.Sources;
using Vogen;

namespace Occtoo.Cards;

/// <summary>Where a card definition is in its lifecycle.</summary>
public enum CardDefinitionStatus
{
    /// <summary>Saved, never activated: no entries yet.</summary>
    Draft,

    /// <summary>Its card properties are live.</summary>
    Active,

    /// <summary>An activation is rebuilding its entries.</summary>
    Updating,

    /// <summary>Archived.</summary>
    Archived,

    /// <summary>Being archived.</summary>
    Archiving,
}

/// <summary>What a card property filled from a managed tag list stores.</summary>
public enum ManagedTagValuePart
{
    /// <summary>The tag's text.</summary>
    Value,

    /// <summary>The tag's key.</summary>
    Key,
}

/// <summary>A card definition's name, shown in Studio: not blank.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct CardDefinitionName
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator CardDefinitionName(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input) ? Validation.Invalid("A card definition name must not be empty.") : Validation.Ok;
}

/// <summary>The managed tag list whose values fill a card property on enrichment.</summary>
public sealed record ManagedTagList(ManagedTagId ListId, ManagedTagValuePart Stores);

/// <summary>
/// A card property: the source property it reads and the type it exposes,
/// which must match the source property's and can't change once active. Build
/// one with <see cref="Of"/>.
/// </summary>
public sealed record CardProperty(
    CardPropertyId Id,
    SourceId SourceId,
    PropertyId SourcePropertyId,
    Maybe<SourcePropertyType> Type,
    IReadOnlyDictionary<LanguageCode, string> Name,
    IReadOnlyDictionary<LanguageCode, string> Description,
    IReadOnlyDictionary<string, string> Settings,
    Maybe<Delimiter> Delimiter,
    decimal Order,
    bool IsHidden,
    bool IsEditable,
    Maybe<ManagedTagList> ManagedTagList)
{
    /// <summary>
    /// A card property reading <paramref name="sourcePropertyId"/> of
    /// <paramref name="sourceId"/>. List types need <see cref="WithDelimiter"/>.
    /// </summary>
    public static CardProperty Of(CardPropertyId id, SourceId sourceId, PropertyId sourcePropertyId, SourcePropertyType type) =>
        new(id, sourceId, sourcePropertyId, type,
            new Dictionary<LanguageCode, string>(), new Dictionary<LanguageCode, string>(), new Dictionary<string, string>(),
            Maybe<Delimiter>.None, 0, false, false, Maybe<ManagedTagList>.None);

    /// <summary>Its display name in a language.</summary>
    public CardProperty Named(LanguageCode language, string name) =>
        this with { Name = new Dictionary<LanguageCode, string>(Name) { [language] = name } };

    /// <summary>Its description in a language.</summary>
    public CardProperty Described(LanguageCode language, string description) =>
        this with { Description = new Dictionary<LanguageCode, string>(Description) { [language] = description } };

    /// <summary>An extra setting, such as <c>timestampFormat</c>.</summary>
    public CardProperty WithSetting(string name, string value) =>
        this with { Settings = new Dictionary<string, string>(Settings) { [name] = value } };

    /// <summary>The delimiter of a <c>List</c> or <c>LocalizedList</c> property.</summary>
    public CardProperty WithDelimiter(Delimiter delimiter) => this with { Delimiter = delimiter };

    /// <summary>Its position among the card properties; 0 keeps an active one's place and appends a new one.</summary>
    public CardProperty At(decimal order) => this with { Order = order };

    /// <summary>Hidden in Studio.</summary>
    public CardProperty Hidden() => this with { IsHidden = true };

    /// <summary>Changeable by enrichment.</summary>
    public CardProperty Editable() => this with { IsEditable = true };

    /// <summary>Filled from a managed tag list on enrichment.</summary>
    public CardProperty FilledFrom(ManagedTagId listId, ManagedTagValuePart stores) =>
        this with { ManagedTagList = new ManagedTagList(listId, stores) };
}

/// <summary>The card properties that present each card in Studio.</summary>
public sealed record CardDisplay(Maybe<CardPropertyId> Name, Maybe<CardPropertyId> Description, Maybe<CardPropertyId> Image);

/// <summary>A group that arranges card properties in Studio; a property is in one group at most.</summary>
public sealed record PropertyGroup(string Name, IReadOnlyList<CardPropertyId> PropertyIds);

/// <summary>Card property changes saved on a card definition, waiting for <see cref="CardDefinitionsClient.Activate"/>.</summary>
public sealed record CardPropertyChanges(IReadOnlyList<CardProperty> Set, IReadOnlyList<CardPropertyId> Remove);

/// <summary>A card definition as lists show it, without its card properties.</summary>
public sealed record CardDefinitionSummary(
    CardDefinitionId Id,
    string Name,
    Maybe<string> Description,
    IReadOnlyList<string> Tags,
    Maybe<CardDefinitionStatus> Status,
    ETag ETag,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A card definition: <c>Properties</c> are the active card properties, empty
/// until the first activation; <c>PendingChanges</c> wait for the next one.
/// </summary>
public sealed record CardDefinition(
    CardDefinitionId Id,
    string Name,
    Maybe<string> Description,
    IReadOnlyList<string> Tags,
    Maybe<CardDefinitionStatus> Status,
    ETag ETag,
    CardDisplay Display,
    IReadOnlyList<PropertyGroup> PropertyGroups,
    IReadOnlyList<CardProperty> Properties,
    Maybe<CardPropertyChanges> PendingChanges,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Starts a change based on this version. Property changes add to the
    /// pending ones already saved.
    /// </summary>
    public UpdateCardDefinition Edit() => new(Id, ETag, PendingChanges.GetValueOrDefault(new CardPropertyChanges([], [])));
}

/// <summary>
/// A new card definition draft. Its card properties wait as pending changes
/// until <see cref="CardDefinitionsClient.Activate"/>.
/// </summary>
public sealed record CreateCardDefinition
{
    private CreateCardDefinition(CardDefinitionId id, CardDefinitionName name)
    {
        Id = id;
        Name = name;
    }

    internal CardDefinitionId Id { get; }

    internal CardDefinitionName Name { get; }

    internal Maybe<string> Description { get; private init; }

    internal IReadOnlyList<string> Tags { get; private init; } = [];

    internal Maybe<CardDisplay> Display { get; private init; }

    internal IReadOnlyList<PropertyGroup> PropertyGroups { get; private init; } = [];

    internal IReadOnlyList<CardProperty> Properties { get; private init; } = [];

    /// <summary>Starts a card definition; its id can't change later.</summary>
    public static CreateCardDefinition WithId(CardDefinitionId id, CardDefinitionName name) => new(id, name);

    /// <summary>A free-text note.</summary>
    public CreateCardDefinition Described(string description) => this with { Description = description };

    /// <summary>Labels for grouping and filtering.</summary>
    public CreateCardDefinition Tagged(params IReadOnlyList<string> tags) => this with { Tags = tags };

    /// <summary>The card properties that present each card.</summary>
    public CreateCardDefinition DisplayedAs(CardDisplay display) => this with { Display = display };

    /// <summary>Groups that arrange the card properties in Studio.</summary>
    public CreateCardDefinition Grouped(params IReadOnlyList<PropertyGroup> groups) => this with { PropertyGroups = groups };

    /// <summary>Adds a card property.</summary>
    public CreateCardDefinition WithProperty(CardProperty property) =>
        this with { Properties = [.. Properties, property ?? throw new ArgumentNullException(nameof(property))] };
}

/// <summary>
/// Changes to a card definition, from <see cref="CardDefinition.Edit"/>,
/// carrying the version they were based on. A change to only the display
/// fields of an active card property applies at once; any other property
/// change waits for activation.
/// </summary>
public sealed record UpdateCardDefinition
{
    internal UpdateCardDefinition(CardDefinitionId id, ETag etag, CardPropertyChanges pending)
    {
        Id = id;
        ETag = etag;
        Pending = pending;
    }

    /// <summary>The card definition to change.</summary>
    public CardDefinitionId Id { get; }

    /// <summary>The version the change is based on.</summary>
    public ETag ETag { get; }

    internal Maybe<CardDefinitionName> Name { get; private init; }

    internal Maybe<string> Description { get; private init; }

    internal Maybe<IReadOnlyList<string>> Tags { get; private init; }

    internal Maybe<CardDisplay> Display { get; private init; }

    internal Maybe<IReadOnlyList<PropertyGroup>> PropertyGroups { get; private init; }

    internal CardPropertyChanges Pending { get; private init; }

    internal bool PendingChanged { get; private init; }

    /// <summary>A new name.</summary>
    public UpdateCardDefinition WithName(CardDefinitionName name) => this with { Name = name };

    /// <summary>A new free-text note; empty clears it.</summary>
    public UpdateCardDefinition WithDescription(string description) => this with { Description = description ?? "" };

    /// <summary>New labels, replacing the old ones.</summary>
    public UpdateCardDefinition WithTags(params IReadOnlyList<string> tags) => this with { Tags = Maybe.From(tags) };

    /// <summary>New display options.</summary>
    public UpdateCardDefinition WithDisplay(CardDisplay display) => this with { Display = display };

    /// <summary>New property groups.</summary>
    public UpdateCardDefinition WithPropertyGroups(params IReadOnlyList<PropertyGroup> groups) =>
        this with { PropertyGroups = Maybe.From(groups) };

    /// <summary>Adds or changes a card property: one that isn't active yet is added, an active one changed.</summary>
    public UpdateCardDefinition SetProperty(CardProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return this with
        {
            Pending = Pending with { Set = [.. Pending.Set.Where(existing => existing.Id != property.Id), property] },
            PendingChanged = true,
        };
    }

    /// <summary>Removes an active card property on the next activation.</summary>
    public UpdateCardDefinition RemoveProperty(CardPropertyId propertyId) =>
        this with
        {
            Pending = new CardPropertyChanges(
                [.. Pending.Set.Where(existing => existing.Id != propertyId)],
                [.. Pending.Remove.Where(existing => existing != propertyId), propertyId]),
            PendingChanged = true,
        };

    /// <summary>Discards every pending card property change.</summary>
    public UpdateCardDefinition DiscardPendingChanges() =>
        this with { Pending = new CardPropertyChanges([], []), PendingChanged = true };
}
