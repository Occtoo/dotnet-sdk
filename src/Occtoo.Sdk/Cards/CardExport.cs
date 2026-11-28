using CSharpFunctionalExtensions;
using Occtoo.Sources;
using Vogen;

namespace Occtoo.Cards;

/// <summary>The file a card export writes.</summary>
public enum CardExportFormat
{
    /// <summary>An Excel workbook.</summary>
    Excel,

    /// <summary>Comma-separated values.</summary>
    Csv,
}

/// <summary>Where a card export is. <c>Completed</c>, <c>Failed</c> and <c>Cancelled</c> are final.</summary>
public enum CardExportStatus
{
    /// <summary>Queued.</summary>
    Pending,

    /// <summary>Writing the file.</summary>
    InProgress,

    /// <summary>Finished; the file is ready.</summary>
    Completed,

    /// <summary>Stopped with an error; start a new export.</summary>
    Failed,

    /// <summary>Cancelled by a user in Studio.</summary>
    Cancelled,
}

/// <summary>The id of a card export.</summary>
[ValueObject<Guid>]
public readonly partial struct CardExportId
{
    private static Validation Validate(Guid input) =>
        input == Guid.Empty ? Validation.Invalid("A card export id must not be empty.") : Validation.Ok;
}

/// <summary>
/// An exported file. <c>Url</c> is a signed download link, made new on every
/// read and valid for up to 24 hours.
/// </summary>
public sealed record CardExportFile(string Name, Maybe<long> SizeBytes, Uri Url);

/// <summary>
/// An export of a card definition's entries to a file. Read it with
/// <see cref="CardDefinitionsClient.GetExport"/> until <c>Status</c> is final;
/// <c>File</c> is present once it is ready and until <c>ExpiresAt</c>.
/// </summary>
public sealed record CardExport(
    CardExportId Id,
    Maybe<CardExportStatus> Status,
    Maybe<double> Progress,
    Maybe<int> TotalEntries,
    Maybe<int> ProcessedEntries,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    Maybe<CardExportFile> File);

/// <summary>
/// What to export: every entry and every visible card property in the
/// tenant's default language, unless narrowed. Fewer than 500,000 entries may
/// match.
/// </summary>
public sealed record StartCardExport
{
    private StartCardExport(CardExportFormat format) => Format = format;

    internal CardExportFormat Format { get; }

    internal Maybe<Where> Where { get; private init; }

    internal Maybe<LanguageCode> WhereLanguage { get; private init; }

    internal Maybe<IReadOnlyList<CardPropertyId>> Properties { get; private init; }

    internal Maybe<IReadOnlyList<LanguageCode>> Languages { get; private init; }

    internal bool IncludeTimestamp { get; private init; }

    internal Maybe<Delimiter> Delimiter { get; private init; }

    internal Maybe<string> Filename { get; private init; }

    /// <summary>An export to a file of this format.</summary>
    public static StartCardExport As(CardExportFormat format) => new(format);

    /// <summary>
    /// Only the entries the condition matches; localized card properties in it
    /// are read in <paramref name="language"/> (default <c>en</c>). It doesn't
    /// pick the exported languages.
    /// </summary>
    public StartCardExport Matching(Where where, Maybe<LanguageCode> language = default) =>
        this with { Where = where ?? throw new ArgumentNullException(nameof(where)), WhereLanguage = language };

    /// <summary>Only these card properties; hidden ones are left out otherwise.</summary>
    public StartCardExport WithProperties(params IReadOnlyList<CardPropertyId> properties) =>
        this with { Properties = Maybe.From(Cards.Where.NonEmpty(properties, "property")) };

    /// <summary>These languages instead of the tenant's default.</summary>
    public StartCardExport InLanguages(params IReadOnlyList<LanguageCode> languages) =>
        this with { Languages = Maybe.From(Cards.Where.NonEmpty(languages, "language")) };

    /// <summary>Adds each entry's timestamp to the file.</summary>
    public StartCardExport WithTimestamps() => this with { IncludeTimestamp = true };

    /// <summary>Joins list values with this delimiter instead of each property's own.</summary>
    public StartCardExport JoinedBy(Delimiter delimiter) => this with { Delimiter = delimiter };

    /// <summary>The file's name without extension; the card definition id otherwise.</summary>
    public StartCardExport Named(string filename) =>
        this with { Filename = string.IsNullOrWhiteSpace(filename) ? throw new ArgumentException("A filename must not be empty.", nameof(filename)) : filename };
}
