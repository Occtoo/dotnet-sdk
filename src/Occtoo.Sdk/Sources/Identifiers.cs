using Vogen;

namespace Occtoo.Sources;

// The ingest identifiers, as value objects. Construction validates the same
// limits the API enforces, so a bad id fails at the call site that made it
// rather than as a 400 after a network round trip. Use `From` when the input is
// trusted (it throws ValueObjectValidationException, in the same spirit as
// options validation) and `TryFrom` when it came from the outside.
//
// These four also convert implicitly from string — the conversion goes through
// the same validation — so call sites can write the literal while every
// signature still names the type.

/// <summary>
/// The customer-facing id of a source within the authenticated tenant — the
/// <c>{sourceId}</c> in <c>POST /sources/{sourceId}</c>. At most 256 characters.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct SourceId
{
    /// <summary>
    /// Converts a string through the same validation as <see cref="From"/>,
    /// so a call site can pass the literal while the signature keeps the type.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator SourceId(string value) => From(value);

    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("A source id must not be empty."),
        { Length: > 256 } => Validation.Invalid("A source id must be at most 256 characters."),
        _ => Validation.Ok,
    };
}

/// <summary>
/// The id of a source entry, unique within its source. At most 256 characters.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct EntryId
{
    /// <summary>
    /// Converts a string through the same validation as <see cref="From"/>,
    /// so a call site can pass the literal while the signature keeps the type.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator EntryId(string value) => From(value);

    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("An entry id must not be empty."),
        { Length: > 256 } => Validation.Invalid("An entry id must be at most 256 characters."),
        _ => Validation.Ok,
    };
}

/// <summary>
/// The id of a source property. At most 256 characters. Occtoo matches property
/// ids case-insensitively and stores them in lowercase, so the id is lowercased
/// on construction.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct PropertyId
{
    /// <summary>
    /// Converts a string through the same validation as <see cref="From"/>,
    /// so a call site can pass the literal while the signature keeps the type.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator PropertyId(string value) => From(value);

    private static string NormalizeInput(string input) => input.ToLowerInvariant();

    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("A property id must not be empty."),
        { Length: > 256 } => Validation.Invalid("A property id must be at most 256 characters."),
        _ => Validation.Ok,
    };
}

/// <summary>
/// The language of a localized value, as an ISO-based code: an ISO 639-1
/// language, optionally followed by an ISO 15924 script and/or an ISO 3166
/// region — <c>en</c>, <c>sv-SE</c>, <c>zh-Hans-CN</c>. Normalized to the
/// conventional casing on construction.
/// </summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct LanguageCode
{
    /// <summary>
    /// Converts a string through the same validation as <see cref="From"/>,
    /// so a call site can pass the literal while the signature keeps the type.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator LanguageCode(string value) => From(value);

    private static string NormalizeInput(string input) => Iso.Normalize(input);

    private static Validation Validate(string input) =>
        Iso.IsValid(input)
            ? Validation.Ok
            : Validation.Invalid(
                $"'{input}' is not an ISO language code such as 'en', 'sv-SE' or 'zh-Hans-CN'.");

    private static class Iso
    {
        // ISO 639-1 two-letter language codes.
        private static readonly HashSet<string> Languages =
        [
            "aa", "ab", "ae", "af", "ak", "am", "an", "ar", "as", "av", "ay", "az",
            "ba", "be", "bg", "bh", "bi", "bm", "bn", "bo", "br", "bs",
            "ca", "ce", "ch", "co", "cr", "cs", "cu", "cv", "cy",
            "da", "de", "dv", "dz", "ee", "el", "en", "eo", "es", "et", "eu",
            "fa", "ff", "fi", "fj", "fo", "fr", "fy", "ga", "gd", "gl", "gn", "gu", "gv",
            "ha", "he", "hi", "ho", "hr", "ht", "hu", "hy", "hz",
            "ia", "id", "ie", "ig", "ii", "ik", "io", "is", "it", "iu",
            "ja", "jv", "ka", "kg", "ki", "kj", "kk", "kl", "km", "kn", "ko", "kr", "ks", "ku", "kv", "kw", "ky",
            "la", "lb", "lg", "li", "ln", "lo", "lt", "lu", "lv",
            "mg", "mh", "mi", "mk", "ml", "mn", "mr", "ms", "mt", "my",
            "na", "nb", "nd", "ne", "ng", "nl", "nn", "no", "nr", "nv", "ny",
            "oc", "oj", "om", "or", "os", "pa", "pi", "pl", "ps", "pt",
            "qu", "rm", "rn", "ro", "ru", "rw",
            "sa", "sc", "sd", "se", "sg", "si", "sk", "sl", "sm", "sn", "so", "sq", "sr", "ss", "st", "su", "sv", "sw",
            "ta", "te", "tg", "th", "ti", "tk", "tl", "tn", "to", "tr", "ts", "tt", "tw", "ty",
            "ug", "uk", "ur", "uz", "ve", "vi", "vo", "wa", "wo", "xh", "yi", "yo", "za", "zh", "zu",
        ];

        internal static string Normalize(string input)
        {
            var parts = input.Split('-');
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = (i, parts[i]) switch
                {
                    (0, var language) => language.ToLowerInvariant(),
                    (_, { Length: 4 } script) =>
                        string.Create(4, script, static (span, s) =>
                        {
                            span[0] = char.ToUpperInvariant(s[0]);
                            s.AsSpan(1).ToLowerInvariant(span[1..]);
                        }),
                    (_, var region) => region.ToUpperInvariant(),
                };
            }

            return string.Join('-', parts);
        }

        internal static bool IsValid(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Length > 10)
                return false;

            var parts = input.Split('-');
            if (!Languages.Contains(parts[0]))
                return false;

            return parts switch
            {
                { Length: 1 } => true,
                { Length: 2 } => IsScript(parts[1]) || IsRegion(parts[1]),
                { Length: 3 } => IsScript(parts[1]) && IsRegion(parts[2]),
                _ => false,
            };
        }

        private static bool IsScript(string part) =>
            part is { Length: 4 } && part.All(char.IsAsciiLetter);

        private static bool IsRegion(string part) => part switch
        {
            { Length: 2 } => part.All(char.IsAsciiLetter),
            { Length: 3 } => part.All(char.IsAsciiDigit),
            _ => false,
        };
    }
}

/// <summary>
/// The correlation id Occtoo assigns to an accepted ingest batch. Distinct from
/// the HTTP request id; keep it for diagnostics and support.
/// </summary>
[ValueObject<Guid>]
public readonly partial struct IngestCorrelationId;
