using CSharpFunctionalExtensions;

namespace Occtoo.Sources;

/// <summary>
/// A source entry as persisted, with values typed by the source's current
/// property configuration. Reads reflect completed ingestion, which may lag
/// an accepted ingest request.
/// </summary>
public sealed record StoredSourceEntry(
    EntryId Id,
    IReadOnlyList<StoredProperty> Properties,
    DateTimeOffset LastUpdated);

/// <summary>
/// One stored value of an entry property. Localized properties yield one
/// item per language, all sharing the property's <c>LastUpdated</c>. A
/// property whose type is not configured has no <c>Type</c> and a text
/// value; a cleared value is <see cref="PropertyValue.Clear"/> (an empty
/// list for list types).
/// </summary>
public sealed record StoredProperty(
    PropertyId Id,
    PropertyValue Value,
    Maybe<SourcePropertyType> Type,
    Maybe<Delimiter> Delimiter,
    DateTimeOffset LastUpdated,
    Maybe<LanguageCode> Language);
