using CSharpFunctionalExtensions;

namespace Occtoo.Sources;

/// <summary>Whether a source is live or being purged.</summary>
public enum SourceStatus
{
    /// <summary>The source accepts and serves data.</summary>
    Active,

    /// <summary>The source is being emptied by a purge workflow.</summary>
    Purging,
}

/// <summary>The kind of data a source holds.</summary>
public enum SourceType
{
    /// <summary>Typed entries.</summary>
    Generic,

    /// <summary>Media assets.</summary>
    Media,
}

/// <summary>Whether a property is settled or has background work in flight.</summary>
public enum SourcePropertyState
{
    /// <summary>Settled.</summary>
    Active,

    /// <summary>A type change is being applied — values may be reindexing.</summary>
    Updating,

    /// <summary>Deletion has been accepted and is in progress.</summary>
    Deleting,
}

/// <summary>A source's metadata.</summary>
public sealed record Source(
    SourceId Id,
    string Name,
    Maybe<string> Description,
    SourceStatus Status,
    SourceType Type,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A source property's metadata. <c>Type</c> is absent for properties whose
/// type has not been configured; <c>Delimiter</c> applies to list types.
/// </summary>
public sealed record SourceProperty(
    PropertyId Id,
    string DisplayName,
    Maybe<string> Description,
    Maybe<SourcePropertyType> Type,
    Maybe<Delimiter> Delimiter,
    SourcePropertyState State);

/// <summary>A new generic source.</summary>
public sealed record CreateSource(SourceId Id, string Name)
{
    /// <summary>A free-text description.</summary>
    public Maybe<string> Description { get; init; } = Maybe<string>.None;
}

/// <summary>
/// Changes to a source's metadata. Absent fields are left unchanged; an
/// empty <c>Description</c> clears it.
/// </summary>
public sealed record UpdateSource
{
    /// <summary>The new name.</summary>
    public Maybe<string> Name { get; init; } = Maybe<string>.None;

    /// <summary>The new description; empty clears it.</summary>
    public Maybe<string> Description { get; init; } = Maybe<string>.None;
}

/// <summary>
/// Creates or updates a property's metadata. Absent fields keep their
/// current values; an empty <c>Description</c> clears it. List types
/// require a <c>Delimiter</c>. A type change may reindex asynchronously —
/// the property reports <see cref="SourcePropertyState.Updating"/> until
/// it settles.
/// </summary>
public sealed record UpsertSourceProperty(string DisplayName)
{
    /// <summary>The value type.</summary>
    public Maybe<SourcePropertyType> Type { get; init; } = Maybe<SourcePropertyType>.None;

    /// <summary>The list delimiter, for list types.</summary>
    public Maybe<Delimiter> Delimiter { get; init; } = Maybe<Delimiter>.None;

    /// <summary>A free-text description; empty clears it.</summary>
    public Maybe<string> Description { get; init; } = Maybe<string>.None;
}

/// <summary>
/// Narrows and pages a source listing. Filters combine with AND; timestamp
/// bounds are inclusive. Keep the same filters when following
/// <see cref="Page"/>'s cursor.
/// </summary>
public sealed record SourceListQuery
{
    /// <summary>Case-insensitive substring of the name.</summary>
    public Maybe<string> Name { get; init; } = Maybe<string>.None;

    /// <summary>The kind of source.</summary>
    public Maybe<SourceType> Type { get; init; } = Maybe<SourceType>.None;

    /// <summary>The source's status. Soft-deleted sources are never listed.</summary>
    public Maybe<SourceStatus> Status { get; init; } = Maybe<SourceStatus>.None;

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
