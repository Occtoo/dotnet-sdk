using System.Diagnostics.CodeAnalysis;
using CSharpFunctionalExtensions;

namespace Occtoo.Sources;

/// <summary>
/// The configured type of a source property.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "The members mirror Occtoo's own property type names.")]
public enum SourcePropertyType
{
    /// <summary>A string value.</summary>
    Text,

    /// <summary>A string value with a language.</summary>
    LocalizedText,

    /// <summary>An array of strings.</summary>
    List,

    /// <summary>An array of strings with a language.</summary>
    LocalizedList,

    /// <summary>A boolean value.</summary>
    Boolean,

    /// <summary>An ISO 8601 point in time.</summary>
    Timestamp,

    /// <summary>A whole number within the 64-bit integer range.</summary>
    Integer,

    /// <summary>A finite decimal number.</summary>
    Decimal,
}

/// <summary>
/// A property that was absent from the source configuration and whose type
/// Occtoo inferred from this request.
/// </summary>
/// <param name="Id">The property id, canonicalized to lowercase.</param>
/// <param name="Type">The inferred type.</param>
/// <param name="Delimiter">The configured delimiter, for inferred list properties.</param>
public sealed record InferredProperty(PropertyId Id, SourcePropertyType Type, Maybe<string> Delimiter);

/// <summary>
/// Occtoo's acknowledgement of an accepted ingest batch.
/// </summary>
/// <remarks>
/// Acceptance means the complete batch passed validation and was queued —
/// downstream entry processing and the registration of
/// <see cref="NewProperties"/> may still be running, so the data is not
/// necessarily visible yet. Do not retry an accepted batch just because it is
/// not visible; keep <see cref="CorrelationId"/> for diagnostics instead.
/// </remarks>
/// <param name="CorrelationId">The id assigned to this asynchronous batch.</param>
/// <param name="SourceId">The source that accepted the entries.</param>
/// <param name="AcceptedAt">When the batch was accepted and queued, in UTC.</param>
/// <param name="AcceptedEntryCount">How many entries the all-or-nothing request contained.</param>
/// <param name="NewProperties">
/// Properties this request introduced, with their inferred types. Empty when
/// every property was already configured.
/// </param>
public sealed record IngestReceipt(
    IngestCorrelationId CorrelationId,
    SourceId SourceId,
    DateTimeOffset AcceptedAt,
    int AcceptedEntryCount,
    IReadOnlyList<InferredProperty> NewProperties);
