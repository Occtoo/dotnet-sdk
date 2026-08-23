using System.Collections.Frozen;

namespace Occtoo.Events;

/// <summary>
/// The mapping between event records and their published type names — the one
/// place the parser and the filter builder agree on.
/// </summary>
internal static class EventTypeCatalog
{
    internal static readonly FrozenDictionary<string, Type> ByName = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["source.created"] = typeof(SourceCreated),
        ["source.updated"] = typeof(SourceUpdated),
        ["source.deleted"] = typeof(SourceDeleted),
        ["source_entry.added"] = typeof(SourceEntryAdded),
        ["source_entry.updated"] = typeof(SourceEntryUpdated),
        ["source_entry.deleted"] = typeof(SourceEntryDeleted),
        ["card.updated"] = typeof(CardUpdated),
        ["card.segment_membership_changed"] = typeof(CardSegmentMembershipChanged),
        ["card_definition.created"] = typeof(CardDefinitionCreated),
        ["card_definition.updated"] = typeof(CardDefinitionUpdated),
        ["card_definition.activated"] = typeof(CardDefinitionActivated),
        ["card_definition.deleted"] = typeof(CardDefinitionDeleted),
        ["card_definition.restored"] = typeof(CardDefinitionRestored),
        ["segment.created"] = typeof(SegmentCreated),
        ["segment.updated"] = typeof(SegmentUpdated),
        ["segment.archived"] = typeof(SegmentArchived),
        ["segment.restored"] = typeof(SegmentRestored),
        ["segment.deleted"] = typeof(SegmentDeleted),
        ["destination.created"] = typeof(DestinationCreated),
        ["destination.published"] = typeof(DestinationPublished),
        ["destination.stopped"] = typeof(DestinationStopped),
        ["destination.deleted"] = typeof(DestinationDeleted),
        ["destination_entry.updated"] = typeof(DestinationEntryUpdated),
        ["destination_entry.deleted"] = typeof(DestinationEntryDeleted),
        ["endpoint.created"] = typeof(EndpointCreated),
        ["endpoint.updated"] = typeof(EndpointUpdated),
        ["endpoint.deleted"] = typeof(EndpointDeleted),
        ["user.created"] = typeof(UserCreated),
        ["user.updated"] = typeof(UserUpdated),
        ["user.deleted"] = typeof(UserDeleted),
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Type, string> ByType =
        ByName.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>
    /// The published type names <typeparamref name="T"/> covers: one for a
    /// sealed event record, all members for an abstract family base.
    /// </summary>
    internal static IReadOnlyList<string> NamesFor<T>()
        where T : CloudEvent
    {
        if (ByType.TryGetValue(typeof(T), out var name))
            return [name];

        var members = ByType
            .Where(pair => typeof(T).IsAssignableFrom(pair.Key))
            .Select(pair => pair.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return members.Length > 0
            ? members
            : throw new InvalidOperationException(
                $"{typeof(T).Name} maps to no published event type.");
    }
}
