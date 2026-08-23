using System.Text.Json;
using CSharpFunctionalExtensions;
using Occtoo.Sources;

namespace Occtoo.Events;

/// <summary>Any <c>source.*</c> event.</summary>
public abstract record SourceEvent(SourceId SourceId) : CloudEvent, IFilterableBySource;

/// <summary>A source was created.</summary>
public sealed record SourceCreated(SourceId SourceId) : SourceEvent(SourceId);

/// <summary>
/// A source configuration changed. <c>Changes</c> names the areas that
/// changed; <c>Properties</c> details the property changes, when there were
/// any.
/// </summary>
public sealed record SourceUpdated(
    SourceId SourceId,
    IReadOnlyList<string> Changes,
    Maybe<IReadOnlyList<SourcePropertyChange>> Properties) : SourceEvent(SourceId);

/// <summary>A source was deleted.</summary>
public sealed record SourceDeleted(SourceId SourceId) : SourceEvent(SourceId);

/// <summary>
/// Any <c>source_entry.*</c> event. <c>EntryKey</c> is the entry's key within
/// its source; <c>Version</c> is the entry's monotonic version.
/// </summary>
public abstract record SourceEntryEvent(SourceId SourceId, EntryId EntryKey, long Version)
    : CloudEvent, IFilterableBySource;

/// <summary>
/// A source entry was added. <c>Properties</c> carries the entry as published,
/// raw — the shape follows the event's <c>dataschema</c>.
/// </summary>
public sealed record SourceEntryAdded(
    SourceId SourceId,
    EntryId EntryKey,
    long Version,
    Maybe<JsonElement> Properties) : SourceEntryEvent(SourceId, EntryKey, Version);

/// <summary>
/// A source entry changed. <c>Values</c> holds the changed values keyed by
/// property name, unless the payload was dropped for size
/// (<c>ValuesTruncated</c>).
/// </summary>
public sealed record SourceEntryUpdated(
    SourceId SourceId,
    EntryId EntryKey,
    long Version,
    IReadOnlyList<PropertyId> ChangedProperties,
    bool ValuesTruncated,
    Maybe<IReadOnlyDictionary<string, IReadOnlyList<EventPropertyValue>>> Values)
    : SourceEntryEvent(SourceId, EntryKey, Version);

/// <summary>A source entry was deleted.</summary>
public sealed record SourceEntryDeleted(SourceId SourceId, EntryId EntryKey, long Version)
    : SourceEntryEvent(SourceId, EntryKey, Version);
