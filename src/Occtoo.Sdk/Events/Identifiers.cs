using Vogen;

namespace Occtoo.Events;

// The event identifiers, as value objects. Response-side ids validate only
// non-emptiness; the string implicit conversions run the same validation as
// `From`, so filter builders can take literals without losing the types.

/// <summary>
/// An opaque position in the tenant event stream — the <c>after</c> value
/// returned by a pull page, or the raw fixed-width <c>sequence</c> of the last
/// successfully processed event.
/// </summary>
/// <remarks>
/// Persist a cursor only after its page has been processed, and persist the
/// filter it was earned under alongside it: a cursor identifies a position in
/// the tenant stream, not in a filtered view, so resuming the same position
/// with a broader filter silently skips events.
/// </remarks>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct EventCursor
{
    /// <summary>
    /// Converts a string through the same validation as <see cref="From"/>.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator EventCursor(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("An event cursor must not be empty.")
            : Validation.Ok;
}

/// <summary>
/// The fixed-width, lexicographically ordered sequence of an event within the
/// tenant stream. Comparable; also accepted by <see cref="EventCursor"/> for
/// recovery when a cursor was lost.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct EventSequence
{
    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("An event sequence must not be empty.")
            : Validation.Ok;

    /// <summary>The cursor form of this sequence, for resuming.</summary>
    public EventCursor AsCursor() => EventCursor.From(Value);
}

/// <summary>The id of a card definition.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct CardDefinitionId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator CardDefinitionId(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("A card definition id must not be empty.")
            : Validation.Ok;
}

/// <summary>The id of an individual card.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct CardId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator CardId(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("A card id must not be empty.")
            : Validation.Ok;
}

/// <summary>The id of a segment.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct SegmentId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator SegmentId(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("A segment id must not be empty.")
            : Validation.Ok;
}

/// <summary>The id of a segment definition.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct SegmentDefinitionId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator SegmentDefinitionId(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("A segment definition id must not be empty.")
            : Validation.Ok;
}

/// <summary>The id of a destination.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct DestinationId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator DestinationId(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("A destination id must not be empty.")
            : Validation.Ok;
}

/// <summary>The id of a destination API version.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct ApiVersionId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator ApiVersionId(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("An API version id must not be empty.")
            : Validation.Ok;
}

/// <summary>The id of a destination endpoint.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct EndpointId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator EndpointId(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("An endpoint id must not be empty.")
            : Validation.Ok;
}

/// <summary>The id of a tenant user.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct UserId
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator UserId(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input)
            ? Validation.Invalid("A user id must not be empty.")
            : Validation.Ok;
}
