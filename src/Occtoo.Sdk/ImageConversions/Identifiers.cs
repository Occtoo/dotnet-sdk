using System.Text.RegularExpressions;
using Vogen;

namespace Occtoo.ImageConversions;

/// <summary>
/// The id of an uploaded original. <see cref="ImageConversionsClient.Upload"/>
/// returns it, and every <see cref="ImageConversionsClient.Convert"/> of that
/// original takes it.
/// </summary>
[ValueObject<Guid>]
public readonly partial struct ImageConversionId
{
    private static Validation Validate(Guid input) =>
        input == Guid.Empty
            ? Validation.Invalid("An image conversion id must not be empty.")
            : Validation.Ok;
}

/// <summary>
/// The shape of the canvas as <c>width:height</c>, such as <c>4:3</c> or
/// <c>1:1</c>. Each side is a whole number from 1 to 100, without leading
/// zeros.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct ImageAspectRatio
{
    /// <summary>
    /// Converts a string through the same validation as <see cref="From"/>,
    /// so a call site can pass the literal while the signature keeps the type.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator ImageAspectRatio(string value) => From(value);

    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("An aspect ratio must not be empty."),
        _ when Pattern().IsMatch(input) => Validation.Ok,
        _ when input.Count(character => character == ':') != 1 => Validation.Invalid(
            $"'{input}' isn't a valid aspect ratio. Write it as width:height, such as 4:3."),
        _ => Validation.Invalid(
            $"'{input}' isn't a valid aspect ratio. Each side must be a whole number from 1 to 100, without leading zeros."),
    };

    [GeneratedRegex(@"^(100|[1-9][0-9]?):(100|[1-9][0-9]?)\z")]
    private static partial Regex Pattern();
}

/// <summary>
/// The color behind transparent areas and padding: six hexadecimal digits in
/// either case, without a <c>#</c>, such as <c>FFFFFF</c>.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct ImageBackground
{
    /// <summary>
    /// Converts a string through the same validation as <see cref="From"/>,
    /// so a call site can pass the literal while the signature keeps the type.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator ImageBackground(string value) => From(value);

    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("A background color must not be empty."),
        _ when Pattern().IsMatch(input) => Validation.Ok,
        _ when input.StartsWith('#') => Validation.Invalid(
            $"'{input}' isn't a valid background color. Leave out the '#'."),
        _ => Validation.Invalid(
            $"'{input}' isn't a valid background color. It must be six hexadecimal digits, such as FFFFFF."),
    };

    [GeneratedRegex(@"^[0-9a-fA-F]{6}\z")]
    private static partial Regex Pattern();
}
