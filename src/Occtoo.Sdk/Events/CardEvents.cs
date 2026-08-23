using Occtoo.Sources;

namespace Occtoo.Events;

/// <summary>Any <c>card.*</c> event.</summary>
public abstract record CardEvent(CardDefinitionId CardDefinitionId, CardId CardId)
    : CloudEvent, IFilterableByCardDefinition;

/// <summary>A card changed as a result of source data.</summary>
public sealed record CardUpdated(
    CardDefinitionId CardDefinitionId,
    CardId CardId,
    IReadOnlyList<PropertyId> ChangedProperties) : CardEvent(CardDefinitionId, CardId);

/// <summary>A card entered or left a segment; <c>Change</c> says which.</summary>
public sealed record CardSegmentMembershipChanged(
    CardDefinitionId CardDefinitionId,
    CardId CardId,
    SegmentId SegmentId,
    string Change) : CardEvent(CardDefinitionId, CardId), IFilterableBySegment;

/// <summary>Any <c>card_definition.*</c> event.</summary>
public abstract record CardDefinitionEvent(CardDefinitionId CardDefinitionId)
    : CloudEvent, IFilterableByCardDefinition;

/// <summary>A card definition was created.</summary>
public sealed record CardDefinitionCreated(CardDefinitionId CardDefinitionId)
    : CardDefinitionEvent(CardDefinitionId);

/// <summary>A card definition changed; <c>Changes</c> names the areas.</summary>
public sealed record CardDefinitionUpdated(
    CardDefinitionId CardDefinitionId,
    IReadOnlyList<string> Changes) : CardDefinitionEvent(CardDefinitionId);

/// <summary>A card definition became active.</summary>
public sealed record CardDefinitionActivated(CardDefinitionId CardDefinitionId)
    : CardDefinitionEvent(CardDefinitionId);

/// <summary>A card definition was deleted.</summary>
public sealed record CardDefinitionDeleted(CardDefinitionId CardDefinitionId)
    : CardDefinitionEvent(CardDefinitionId);

/// <summary>A card definition was restored.</summary>
public sealed record CardDefinitionRestored(CardDefinitionId CardDefinitionId)
    : CardDefinitionEvent(CardDefinitionId);
