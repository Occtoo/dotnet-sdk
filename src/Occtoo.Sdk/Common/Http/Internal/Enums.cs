using CSharpFunctionalExtensions;

namespace Occtoo.Http.Internal;

/// <summary>
/// Reads enum names from API responses leniently: a value this SDK version
/// does not know — one the platform added later — reads as absent instead of
/// failing the whole response.
/// </summary>
internal static class Enums
{
    internal static Maybe<TEnum> Read<TEnum>(string? name)
        where TEnum : struct, Enum =>
        name is { Length: > 0 }
        && !char.IsAsciiDigit(name[0])
        && Enum.TryParse<TEnum>(name, ignoreCase: true, out var value)
        && Enum.IsDefined(value)
            ? Maybe.From(value)
            : Maybe<TEnum>.None;
}
