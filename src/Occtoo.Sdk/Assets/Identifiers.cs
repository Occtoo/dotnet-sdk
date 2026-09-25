using System.Text.RegularExpressions;
using Occtoo.Sources;
using Vogen;

namespace Occtoo.Assets;

// The asset identifiers, as value objects, following the same pattern as the
// ingest ones: construction validates what the API enforces, `From` throws and
// `TryFrom` reports. The asset surface is stricter than typed ingest in two
// places — the entry key's character set and the filename's rules — so it has
// its own types rather than reusing EntryId.

/// <summary>
/// The key of one asset within its data source — the id of the data source
/// entry the upload creates. Letters, digits, <c>_</c> and <c>-</c> only, at
/// most 256 characters.
/// </summary>
/// <remarks>
/// Stricter than <see cref="EntryId"/>, which still accepts the wider character
/// set that sources created before the asset surface existed were allowed to
/// use. The key is stored and echoed back exactly as given, so it is never
/// normalized; Occtoo compares it case-insensitively when checking a batch for
/// duplicates.
/// </remarks>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct AssetKey
{
    /// <summary>The longest key Occtoo accepts.</summary>
    public const int MaxLength = 256;

    /// <summary>
    /// Converts a string through the same validation as <see cref="From"/>,
    /// so a call site can pass the literal while the signature keeps the type.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator AssetKey(string value) => From(value);

    /// <summary>
    /// The same value as the id of the data source entry the upload creates,
    /// for reading or writing that entry through <see cref="OcctooClient.Sources"/>.
    /// </summary>
    public EntryId AsEntryId() => EntryId.From(Value);

    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("An asset key must not be empty."),
        { Length: > MaxLength } => Validation.Invalid($"An asset key must be at most {MaxLength} characters."),
        _ when !IsAllowed(input) => Validation.Invalid(
            $"'{input}' is not a valid asset key: keys allow letters, digits, '_' and '-'."),
        _ => Validation.Ok,
    };

    // A span scan rather than a regex: the rule is an ASCII character set, and
    // char.IsAsciiLetterOrDigit carries no culture or ICU dependency.
    private static bool IsAllowed(string input)
    {
        foreach (var character in input.AsSpan())
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))
                return false;
        }

        return true;
    }
}

/// <summary>
/// The name the asset's file takes in Occtoo, and the last segment of the blob
/// the bytes are uploaded to. At most 200 characters.
/// </summary>
/// <remarks>
/// The same name must be used to initialize and to complete an asset: it is
/// part of the blob's path, so completing under a different name points at a
/// blob that does not exist. Passing <see cref="Asset"/> to both calls is what
/// keeps that true without the caller having to remember it.
/// </remarks>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct AssetFilename
{
    /// <summary>The longest filename Occtoo accepts.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Converts a string through the same validation as <see cref="From"/>,
    /// so a call site can pass the literal while the signature keeps the type.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator AssetFilename(string value) => From(value);

    /// <summary>
    /// The filename part of a path, as <see cref="Path.GetFileName(string)"/>
    /// reads it — the form <see cref="AssetUpload.FromFile(AssetKey, string)"/>
    /// uses.
    /// </summary>
    /// <exception cref="ValueObjectValidationException">The path ends in no usable filename.</exception>
    public static AssetFilename FromPath(string path) => From(Path.GetFileName(path));

    // Mirrors the server's own rule: anything except a path separator or a
    // control character, not starting or ending with a space or '.', and no
    // '..' anywhere. GeneratedRegex is the AOT-safe form.
    [GeneratedRegex(@"^(?![ .])(?!.*\.\.)[^\x00-\x1F\x7F/\\]+(?<![ .])$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Allowed();

    // Each rule reports itself: this is the value object a caller trips over
    // most, and "invalid filename" does not say what to change.
    private static Validation Validate(string input) => input switch
    {
        _ when string.IsNullOrWhiteSpace(input) => Validation.Invalid("A filename must not be empty."),
        { Length: > MaxLength } => Validation.Invalid($"A filename must be at most {MaxLength} characters."),
        _ when input.AsSpan().IndexOfAny('/', '\\') >= 0 => Validation.Invalid(
            $"'{input}' is not a valid filename: it must name a file, not a path, so '/' and '\\' are not allowed."),
        _ when input.StartsWith(' ') || input.StartsWith('.') => Validation.Invalid(
            $"'{input}' is not a valid filename: it must not start with a space or a '.'."),
        _ when input.EndsWith(' ') || input.EndsWith('.') => Validation.Invalid(
            $"'{input}' is not a valid filename: it must not end with a space or a '.'."),
        _ when input.Contains("..", StringComparison.Ordinal) => Validation.Invalid(
            $"'{input}' is not a valid filename: it must not contain '..'."),
        _ when !Allowed().IsMatch(input) => Validation.Invalid(
            $"'{input}' is not a valid filename: it must not contain control characters."),
        _ => Validation.Ok,
    };
}

/// <summary>
/// The id of a folder within a data source. Optional wherever it appears —
/// omitting it puts the entries in the data source's root folder.
/// </summary>
[ValueObject<Guid>]
public readonly partial struct FolderId
{
    private static Validation Validate(Guid input) =>
        input == Guid.Empty
            ? Validation.Invalid("A folder id must not be empty.")
            : Validation.Ok;
}
