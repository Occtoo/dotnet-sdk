using Vogen;

namespace Occtoo.Events;

// The event identifiers, as value objects. Response-side ids validate only
// non-emptiness; the string implicit conversions run the same validation as
// `From`, so filter builders can take literals without losing the types.

/// <summary>
/// The fixed-width, lexicographically ordered sequence of an event within the
/// tenant stream. Comparable; also accepted by <see cref="PageCursor"/> for
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
    public PageCursor AsCursor() => PageCursor.From(Value);
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

/// <summary>
/// The id of an event destination — the durable consumer a worker pulls
/// leased batches from.
/// </summary>
[ValueObject<Guid>]
public readonly partial struct EventDestinationId
{
    private static Validation Validate(Guid input) =>
        input == Guid.Empty ? Validation.Invalid("An event destination id must not be empty.") : Validation.Ok;
}

/// <summary>
/// Identifies one worker pulling from a durable consumer: not blank, at most
/// 200 characters.
/// </summary>
/// <remarks>
/// <para>
/// Give every concurrently running pull loop its own id. A pull from a worker
/// that still holds a lease resumes that lease — the API reads it as "the
/// previous batch was abandoned" — so two loops sharing an id hand each other's
/// batches back and forth.
/// </para>
/// <para>
/// Keep the id stable across restarts of the same worker (a pod name plus a
/// loop index, say): a restarted worker then resumes its own abandoned lease
/// immediately instead of waiting for it to expire.
/// </para>
/// </remarks>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct EventWorkerId
{
    /// <summary>The API's length ceiling for a worker id.</summary>
    public const int MaxLength = 200;

    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator EventWorkerId(string value) => From(value);

    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("An event worker id must not be empty."),
        { Length: > MaxLength } => Validation.Invalid($"An event worker id must be at most {MaxLength} characters."),
        _ => Validation.Ok,
    };
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
