using CSharpFunctionalExtensions;
using Vogen;

namespace Occtoo.ManagedTags;

/// <summary>The id of a managed tag.</summary>
[ValueObject<Guid>]
public readonly partial struct ManagedTagId
{
    private static Validation Validate(Guid input) =>
        input == Guid.Empty ? Validation.Invalid("A managed tag id must not be empty.") : Validation.Ok;
}

/// <summary>
/// The key of a value within a managed tag — one URL path segment: no
/// slashes, backslashes, or control characters, and not <c>.</c> or
/// <c>..</c>.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct ManagedTagValueKey
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator ManagedTagValueKey(string value) => From(value);

    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("A managed tag value key must not be empty."),
        "." or ".." => Validation.Invalid("A managed tag value key must not be a dot segment."),
        _ when input.Any(c => c is '/' or '\\' || char.IsControl(c)) =>
            Validation.Invalid("A managed tag value key must be a single URL path segment without slashes or control characters."),
        _ => Validation.Ok,
    };
}

/// <summary>What kind of values a managed tag holds. Immutable once created.</summary>
public enum ManagedTagType
{
    /// <summary>One string per value.</summary>
    Text,

    /// <summary>One string per language per value.</summary>
    LocalizedText,
}

/// <summary>
/// A managed tag: a named, optionally hierarchical list of values that
/// cards reference. <c>ParentId</c> nests it under another managed tag.
/// </summary>
public sealed record ManagedTag(
    ManagedTagId Id,
    string DisplayName,
    ManagedTagType Type,
    Maybe<ManagedTagId> ParentId,
    DateTimeOffset CreatedAt,
    Maybe<DateTimeOffset> LastModifiedAt,
    Maybe<Guid> CreatedBy,
    Maybe<Guid> UpdatedBy);

/// <summary>
/// The content of a managed tag value — a single string for
/// <see cref="ManagedTagType.Text"/> tags, one string per language code for
/// <see cref="ManagedTagType.LocalizedText"/> tags.
/// </summary>
public abstract record ManagedTagValueContent
{
    private protected ManagedTagValueContent()
    {
    }

    /// <summary>A single string, for <see cref="ManagedTagType.Text"/> tags.</summary>
    public static ManagedTagValueContent Text(string value) => new TextValue(value);

    /// <summary>One string per language code, for <see cref="ManagedTagType.LocalizedText"/> tags.</summary>
    public static ManagedTagValueContent Localized(IReadOnlyDictionary<string, string> translations) =>
        new LocalizedValue(translations);

    /// <summary>A single string.</summary>
    public sealed record TextValue(string Value) : ManagedTagValueContent;

    /// <summary>Strings keyed by language code.</summary>
    public sealed record LocalizedValue(IReadOnlyDictionary<string, string> Translations) : ManagedTagValueContent;

    /// <summary>A string literal is a single value.</summary>
    public static implicit operator ManagedTagValueContent(string value) => Text(value);
}

/// <summary>
/// One value of a managed tag. <c>Order</c> is display metadata; lists are
/// paginated by key. <c>ParentKey</c> nests it under another value of the
/// same tag.
/// </summary>
public sealed record ManagedTagValue(
    ManagedTagValueKey Key,
    ManagedTagValueContent Content,
    double Order,
    Maybe<ManagedTagValueKey> ParentKey,
    DateTimeOffset CreatedAt,
    Maybe<DateTimeOffset> LastModifiedAt,
    Guid CreatedBy,
    Maybe<Guid> UpdatedBy);

/// <summary>A new managed tag. Names are unique within the tenant.</summary>
public sealed record CreateManagedTag(string DisplayName, ManagedTagType Type)
{
    /// <summary>The managed tag to nest under.</summary>
    public Maybe<ManagedTagId> ParentId { get; init; } = Maybe<ManagedTagId>.None;
}

/// <summary>
/// Replaces a managed tag's name and parent. The type is immutable, and
/// changing the parent clears the parent links of its values.
/// </summary>
public sealed record UpdateManagedTag(string DisplayName)
{
    /// <summary>The managed tag to nest under; absent un-nests it.</summary>
    public Maybe<ManagedTagId> ParentId { get; init; } = Maybe<ManagedTagId>.None;
}

/// <summary>
/// A new value. <c>Content</c> must match the tag's type; an existing key is
/// a <see cref="ConflictError"/>.
/// </summary>
public sealed record CreateManagedTagValue(ManagedTagValueKey Key, ManagedTagValueContent Content)
{
    /// <summary>Display order; defaults to 0.</summary>
    public double Order { get; init; }

    /// <summary>The value of the same tag to nest under.</summary>
    public Maybe<ManagedTagValueKey> ParentKey { get; init; } = Maybe<ManagedTagValueKey>.None;
}

/// <summary>Replaces a value's content, order, and parent. The key is immutable.</summary>
public sealed record UpdateManagedTagValue(ManagedTagValueContent Content)
{
    /// <summary>Display order; defaults to 0.</summary>
    public double Order { get; init; }

    /// <summary>The value of the same tag to nest under; absent un-nests it.</summary>
    public Maybe<ManagedTagValueKey> ParentKey { get; init; } = Maybe<ManagedTagValueKey>.None;
}

/// <summary>
/// Narrows and pages a managed tag listing. Filters combine with AND;
/// timestamp bounds are inclusive; results are ordered by id.
/// </summary>
public sealed record ManagedTagListQuery
{
    /// <summary>Case-insensitive substring of the display name.</summary>
    public Maybe<string> Name { get; init; } = Maybe<string>.None;

    /// <summary>The value type.</summary>
    public Maybe<ManagedTagType> Type { get; init; } = Maybe<ManagedTagType>.None;

    /// <summary>Only tags nested under this one.</summary>
    public Maybe<ManagedTagId> ParentId { get; init; } = Maybe<ManagedTagId>.None;

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

/// <summary>
/// Narrows and pages a value listing. Results are ordered by key;
/// <see cref="ManagedTagValue.Order"/> does not affect pagination.
/// </summary>
public sealed record ManagedTagValueListQuery
{
    /// <summary>Case-sensitive substring of the key.</summary>
    public Maybe<string> Key { get; init; } = Maybe<string>.None;

    /// <summary>Only values nested under this key.</summary>
    public Maybe<ManagedTagValueKey> ParentKey { get; init; } = Maybe<ManagedTagValueKey>.None;

    /// <summary>Where to read and how much.</summary>
    public PageRequest Page { get; init; } = new();
}
