using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.Sources;
using Vogen;

namespace Occtoo.Cards;

/// <summary>How a segment picks its cards. Fixed when the segment is created.</summary>
public enum SegmentType
{
    /// <summary>Selects cards by a condition and keeps following it.</summary>
    Query,

    /// <summary>Holds a listed set of card ids.</summary>
    Fixed,
}

/// <summary>Where a segment is in its lifecycle.</summary>
public enum SegmentStatus
{
    /// <summary>Entries are being tagged in the background.</summary>
    Initializing,

    /// <summary>Tagging is done.</summary>
    Active,

    /// <summary>Waiting for its start time.</summary>
    Scheduled,

    /// <summary>Archived.</summary>
    Archived,
}

/// <summary>A segment's name: not blank.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct SegmentName
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator SegmentName(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input) ? Validation.Invalid("A segment name must not be empty.") : Validation.Ok;
}

/// <summary>
/// A named set of cards of one card definition. <c>Where</c> is the stored
/// condition: it can differ in form from what was sent, never in meaning.
/// <c>StartTime</c> absent means active as soon as created; <c>EndTime</c>
/// absent means never archived on a schedule.
/// </summary>
public sealed record Segment(
    SegmentId Id,
    SegmentDefinitionId DefinitionId,
    CardDefinitionId CardDefinitionId,
    Maybe<SegmentType> Type,
    Maybe<SegmentStatus> Status,
    string Name,
    Maybe<string> Description,
    Maybe<LanguageCode> Language,
    Maybe<string> Image,
    IReadOnlyList<string> Tags,
    Maybe<Where> Where,
    Maybe<DateTimeOffset> StartTime,
    Maybe<DateTimeOffset> EndTime,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Starts a change; fields left alone stay as they are.</summary>
    public UpdateSegment Edit() => new(CardDefinitionId, Id);
}

/// <summary>
/// A new segment. Its entries are tagged in the background, and it is
/// <see cref="SegmentStatus.Initializing"/> until that finishes.
/// </summary>
public sealed record CreateSegment
{
    private CreateSegment(SegmentId id, SegmentName name, LanguageCode language, Where where, SegmentType type)
    {
        Id = id;
        Name = name;
        Language = language;
        Where = where ?? throw new ArgumentNullException(nameof(where));
        Type = type;
    }

    internal SegmentId Id { get; }

    internal SegmentName Name { get; }

    internal LanguageCode Language { get; }

    internal Where Where { get; }

    internal SegmentType Type { get; }

    internal Maybe<string> Description { get; private init; }

    internal Maybe<string> Image { get; private init; }

    internal Maybe<IReadOnlyList<string>> Tags { get; private init; }

    internal Maybe<DateTimeOffset> StartTime { get; private init; }

    internal Maybe<DateTimeOffset> EndTime { get; private init; }

    /// <summary>
    /// A segment that selects cards by <paramref name="where"/> and keeps
    /// following it; localized card properties in it are read in
    /// <paramref name="language"/>.
    /// </summary>
    public static CreateSegment Query(SegmentId id, SegmentName name, LanguageCode language, Where where) =>
        new(id, name, language, where, SegmentType.Query);

    /// <summary>A segment holding exactly the listed cards.</summary>
    public static CreateSegment Fixed(SegmentId id, SegmentName name, LanguageCode language, params IReadOnlyList<CardId> cards) =>
        new(id, name, language,
            Where.Text("id").In([.. Cards.Where.NonEmpty(cards, "card").Select(card => card.Value)]),
            SegmentType.Fixed);

    /// <summary>A free-text note.</summary>
    public CreateSegment Described(string description) => this with { Description = description };

    /// <summary>An image key: lowercase letters a–z only.</summary>
    public CreateSegment WithImage(string image) => this with { Image = SegmentImages.Check(image) };

    /// <summary>Labels for grouping and filtering: alphanumeric, no duplicates.</summary>
    public CreateSegment Tagged(params IReadOnlyList<string> tags) => this with { Tags = Maybe.From(tags) };

    /// <summary>When the segment becomes active: in the future, on the hour.</summary>
    public CreateSegment ActiveFrom(DateTimeOffset start) => this with { StartTime = start };

    /// <summary>When the segment is archived: after its start, on the hour.</summary>
    public CreateSegment ActiveUntil(DateTimeOffset end) => this with { EndTime = end };
}

/// <summary>
/// Changes to a segment, from <see cref="Segment.Edit"/>. A new condition or
/// language starts tagging again in the background.
/// </summary>
public sealed record UpdateSegment
{
    internal UpdateSegment(CardDefinitionId cardDefinitionId, SegmentId id)
    {
        CardDefinitionId = cardDefinitionId;
        Id = id;
    }

    /// <summary>The card definition the segment belongs to.</summary>
    public CardDefinitionId CardDefinitionId { get; }

    /// <summary>The segment to change.</summary>
    public SegmentId Id { get; }

    internal Maybe<SegmentName> Name { get; private init; }

    internal Maybe<LanguageCode> Language { get; private init; }

    internal Maybe<Where> Where { get; private init; }

    internal Maybe<string> Description { get; private init; }

    internal Maybe<string> Image { get; private init; }

    internal Maybe<IReadOnlyList<string>> Tags { get; private init; }

    internal Maybe<DateTimeOffset> StartTime { get; private init; }

    internal Maybe<DateTimeOffset> EndTime { get; private init; }

    /// <summary>A new name.</summary>
    public UpdateSegment WithName(SegmentName name) => this with { Name = name };

    /// <summary>A new language for localized card properties in the condition.</summary>
    public UpdateSegment WithLanguage(LanguageCode language) => this with { Language = language };

    /// <summary>A new condition.</summary>
    public UpdateSegment WithWhere(Where where) => this with { Where = where ?? throw new ArgumentNullException(nameof(where)) };

    /// <summary>A new free-text note; empty clears it.</summary>
    public UpdateSegment WithDescription(string description) => this with { Description = description ?? "" };

    /// <summary>A new image key: lowercase letters a–z only.</summary>
    public UpdateSegment WithImage(string image) => this with { Image = SegmentImages.Check(image) };

    /// <summary>New labels, replacing the old ones.</summary>
    public UpdateSegment WithTags(params IReadOnlyList<string> tags) => this with { Tags = Maybe.From(tags) };

    /// <summary>A new start time, on the hour; only while the segment is scheduled.</summary>
    public UpdateSegment ActiveFrom(DateTimeOffset start) => this with { StartTime = start };

    /// <summary>A new end time, on the hour.</summary>
    public UpdateSegment ActiveUntil(DateTimeOffset end) => this with { EndTime = end };
}

/// <summary>
/// Narrows and pages a segment listing; archived segments are left out.
/// Keep the same filters when following a cursor.
/// </summary>
public sealed record SegmentListQuery
{
    /// <summary>Case-insensitive substring of the name.</summary>
    public Maybe<string> Name { get; init; } = Maybe<string>.None;

    /// <summary>Only segments of this type.</summary>
    public Maybe<SegmentType> Type { get; init; } = Maybe<SegmentType>.None;

    /// <summary>Only segments in this state.</summary>
    public Maybe<SegmentStatus> Status { get; init; } = Maybe<SegmentStatus>.None;

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

internal static class SegmentImages
{
    internal static string Check(string image) =>
        !string.IsNullOrEmpty(image) && image.All(char.IsAsciiLetterLower)
            ? image
            : throw new ArgumentException("An image key is lowercase letters a-z only.", nameof(image));
}
