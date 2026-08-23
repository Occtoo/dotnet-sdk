using CSharpFunctionalExtensions;

namespace Occtoo.Events;

/// <summary>
/// Any <c>segment.*</c> event. <c>CardDefinitionId</c> is present when the
/// segment is bound to a card definition.
/// </summary>
public abstract record SegmentEvent(
    SegmentId SegmentId,
    SegmentDefinitionId SegmentDefinitionId,
    Maybe<CardDefinitionId> CardDefinitionId)
    : CloudEvent, IFilterableBySegment, IFilterableBySegmentDefinition, IFilterableByCardDefinition;

/// <summary>A segment was created.</summary>
public sealed record SegmentCreated(
    SegmentId SegmentId,
    SegmentDefinitionId SegmentDefinitionId,
    Maybe<CardDefinitionId> CardDefinitionId)
    : SegmentEvent(SegmentId, SegmentDefinitionId, CardDefinitionId);

/// <summary>A segment changed; <c>Changes</c> names the areas.</summary>
public sealed record SegmentUpdated(
    SegmentId SegmentId,
    SegmentDefinitionId SegmentDefinitionId,
    Maybe<CardDefinitionId> CardDefinitionId,
    IReadOnlyList<string> Changes)
    : SegmentEvent(SegmentId, SegmentDefinitionId, CardDefinitionId);

/// <summary>A segment was archived.</summary>
public sealed record SegmentArchived(
    SegmentId SegmentId,
    SegmentDefinitionId SegmentDefinitionId,
    Maybe<CardDefinitionId> CardDefinitionId)
    : SegmentEvent(SegmentId, SegmentDefinitionId, CardDefinitionId);

/// <summary>A segment was restored.</summary>
public sealed record SegmentRestored(
    SegmentId SegmentId,
    SegmentDefinitionId SegmentDefinitionId,
    Maybe<CardDefinitionId> CardDefinitionId)
    : SegmentEvent(SegmentId, SegmentDefinitionId, CardDefinitionId);

/// <summary>A segment was deleted.</summary>
public sealed record SegmentDeleted(
    SegmentId SegmentId,
    SegmentDefinitionId SegmentDefinitionId,
    Maybe<CardDefinitionId> CardDefinitionId)
    : SegmentEvent(SegmentId, SegmentDefinitionId, CardDefinitionId);
