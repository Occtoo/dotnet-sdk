using CSharpFunctionalExtensions;
using Occtoo.ManagedTags;

namespace Occtoo.Events;

/// <summary>Any <c>managed_tag_list.*</c> event. List events never contain values.</summary>
public abstract record ManagedTagListEvent(ManagedTagId ManagedTagId) : CloudEvent, IFilterableByManagedTag;

/// <summary>A managed tag list was created. Carries its complete metadata.</summary>
/// <remarks><c>TagType</c> is the list's value kind, distinct from the envelope's <see cref="CloudEvent.Type"/>. None for an unknown future kind.</remarks>
public sealed record ManagedTagListCreated(
    ManagedTagId ManagedTagId,
    string DisplayName,
    Maybe<ManagedTagType> TagType,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    Maybe<ManagedTagId> ParentId,
    Maybe<Guid> CreatedBy,
    Maybe<Guid> UpdatedBy) : ManagedTagListEvent(ManagedTagId);

/// <summary>A managed tag list changed. If its parent changed, refetch its values: their parent keys were cleared without individual value events.</summary>
/// <remarks><c>TagType</c> is the list's value kind, distinct from the envelope's <see cref="CloudEvent.Type"/>. None for an unknown future kind.</remarks>
public sealed record ManagedTagListUpdated(
    ManagedTagId ManagedTagId,
    string DisplayName,
    Maybe<ManagedTagType> TagType,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    Maybe<ManagedTagId> ParentId,
    Maybe<Guid> CreatedBy,
    Maybe<Guid> UpdatedBy) : ManagedTagListEvent(ManagedTagId);

/// <summary>A list and its values were deleted. Individual value deletion events are not emitted.</summary>
public sealed record ManagedTagListDeleted(ManagedTagId ManagedTagId) : ManagedTagListEvent(ManagedTagId);

/// <summary>Any <c>managed_tag_value.*</c> event. Keys are opaque, case-sensitive strings within a list.</summary>
public abstract record ManagedTagValueEvent(ManagedTagId ManagedTagId, string Key) : CloudEvent, IFilterableByManagedTag;

/// <summary>A value was added to a managed tag list.</summary>
/// <remarks><c>Content</c> holds the full text or translations. When <c>ValueTruncated</c> is true, content was omitted for size: keep stored content and fetch the current value through <c>client.ManagedTags.GetValue</c>.</remarks>
public sealed record ManagedTagValueAdded(
    ManagedTagId ManagedTagId,
    string Key,
    Maybe<ManagedTagValueContent> Content,
    double Order,
    Maybe<string> ParentKey,
    DateTimeOffset CreatedAt,
    Maybe<DateTimeOffset> LastModifiedAt,
    Guid CreatedBy,
    Maybe<Guid> UpdatedBy,
    bool ValueTruncated) : ManagedTagValueEvent(ManagedTagId, Key);

/// <summary>A value's content, order, or parent key changed. Content is a replacement snapshot, including all current translations.</summary>
/// <remarks><c>Content</c> holds the full text or translations. When <c>ValueTruncated</c> is true, content was omitted for size: keep stored content and fetch the current value through <c>client.ManagedTags.GetValue</c>.</remarks>
public sealed record ManagedTagValueUpdated(
    ManagedTagId ManagedTagId,
    string Key,
    Maybe<ManagedTagValueContent> Content,
    double Order,
    Maybe<string> ParentKey,
    DateTimeOffset CreatedAt,
    Maybe<DateTimeOffset> LastModifiedAt,
    Guid CreatedBy,
    Maybe<Guid> UpdatedBy,
    bool ValueTruncated) : ManagedTagValueEvent(ManagedTagId, Key);

/// <summary>A value was deleted directly. Deleting its list emits <see cref="ManagedTagListDeleted"/> instead.</summary>
public sealed record ManagedTagValueDeleted(ManagedTagId ManagedTagId, string Key) : ManagedTagValueEvent(ManagedTagId, Key);
