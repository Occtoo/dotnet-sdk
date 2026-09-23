using Vogen;

namespace Occtoo.Applications;

// Builder inputs as value objects: each validates the limits the API enforces
// and converts implicitly from a string through that same validation.

/// <summary>An application's name: not blank, at most 100 characters.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct ApplicationName
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator ApplicationName(string value) => From(value);

    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("An application name must not be empty."),
        _ when input.Trim().Length > 100 => Validation.Invalid("An application name must be at most 100 characters."),
        _ => Validation.Ok,
    };
}

/// <summary>An application's description: at most 500 characters.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct ApplicationDescription
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator ApplicationDescription(string value) => From(value);

    private static Validation Validate(string input) =>
        input is null || input.Trim().Length > 500
            ? Validation.Invalid("An application description must be at most 500 characters.")
            : Validation.Ok;
}

/// <summary>A label on an application: not blank.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct ApplicationTag
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator ApplicationTag(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrWhiteSpace(input) ? Validation.Invalid("An application tag must not be empty.") : Validation.Ok;
}

/// <summary>
/// A capability to grant — one of the <see cref="Authentication.OcctooScopes"/>
/// constants, which convert implicitly. Not blank, no whitespace.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct ApplicationScope
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator ApplicationScope(string value) => From(value);

    private static Validation Validate(string input) =>
        string.IsNullOrEmpty(input) || input.Any(char.IsWhiteSpace)
            ? Validation.Invalid("A scope must be a single non-empty key such as read:sources.")
            : Validation.Ok;
}
