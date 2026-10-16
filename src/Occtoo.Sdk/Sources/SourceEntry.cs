using CSharpFunctionalExtensions;

namespace Occtoo.Sources;

/// <summary>
/// One property value on a <see cref="SourceEntry"/> or an
/// <see cref="Occtoo.Assets.AssetUpload"/>.
/// </summary>
/// <param name="Id">The source property the value belongs to.</param>
/// <param name="Value">The typed value.</param>
/// <param name="Language">
/// The language, for <c>LocalizedText</c> and <c>LocalizedList</c> properties.
/// Localized properties require it; non-localized properties reject it. The same
/// property id may repeat across distinct languages.
/// </param>
public sealed record EntryProperty(PropertyId Id, PropertyValue Value, Maybe<LanguageCode> Language)
{
    /// <summary>Creates a non-localized property value.</summary>
    /// <param name="id">The source property the value belongs to.</param>
    /// <param name="value">The typed value.</param>
    public EntryProperty(PropertyId id, PropertyValue value)
        : this(id, value, Maybe<LanguageCode>.None)
    {
    }
}

/// <summary>
/// One entry in a typed ingest batch: an upsert built with <see cref="WithId"/>,
/// or a deletion made with <see cref="Delete"/>. Both can share one batch.
/// </summary>
/// <param name="Id">The entry's id, unique within the source.</param>
/// <param name="Properties">The property values to upsert on the entry.</param>
/// <example>
/// <code>
/// var entry = SourceEntry
///     .WithId("sku-123")
///     .WithLocalizedText("name", "Blue chair", "en")
///     .WithDecimal("price", 100.111m)
///     .WithList("tags", "summer", "sale")
///     .WithBoolean("inStock", true);
/// </code>
/// Every id and language above is a value object — <see cref="EntryId"/>,
/// <see cref="PropertyId"/>, <see cref="LanguageCode"/> — reached through an
/// implicit, validating conversion from the string literal.
/// </example>
public sealed record SourceEntry(EntryId Id, IReadOnlyList<EntryProperty> Properties)
{
    /// <summary>
    /// Starts building an entry fluently.
    /// </summary>
    /// <param name="id">
    /// The entry's id, unique within the source. A string converts implicitly
    /// and is validated on the way in.
    /// </param>
    /// <returns>A builder to add properties to.</returns>
    public static EntryPropertyBuilder<SourceEntry> WithId(EntryId id) =>
        new(properties => new SourceEntry(id, properties));

    /// <summary>An entry that deletes <paramref name="id"/> from the source. It carries no properties.</summary>
    public static SourceEntry Delete(EntryId id) => new(id, []) { IsDeletion = true };

    /// <summary>Whether this entry deletes rather than upserts. Only <see cref="Delete"/> sets it.</summary>
    public bool IsDeletion { get; private init; }
}
