using CSharpFunctionalExtensions;

namespace Occtoo.Sources;

/// <summary>
/// One property value on a <see cref="SourceEntry"/>.
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
/// One entry to upsert into a source — typed ingest has no delete flag, so every
/// entry either creates or updates.
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
    public static SourceEntryBuilder WithId(EntryId id) => new(id);
}

/// <summary>
/// Builds a <see cref="SourceEntry"/> fluently. Converts implicitly, so a
/// finished builder can be passed wherever an entry is expected without calling
/// <see cref="Build"/>.
/// </summary>
/// <remarks>
/// Every method takes the value-object types — <see cref="PropertyId"/>,
/// <see cref="LanguageCode"/> — which convert implicitly from strings through
/// their validation, throwing <see cref="Vogen.ValueObjectValidationException"/>
/// for invalid input: the same construction-time contract as the value objects
/// themselves.
/// </remarks>
public sealed class SourceEntryBuilder
{
    private readonly EntryId _id;
    private readonly List<EntryProperty> _properties = [];

    internal SourceEntryBuilder(EntryId id) => _id = id;

    /// <summary>Adds a <c>Text</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The text.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithText(PropertyId propertyId, string value) =>
        WithProperty(new(propertyId, PropertyValue.Text(value)));

    /// <summary>Adds a <c>LocalizedText</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The text.</param>
    /// <param name="language">The language, such as <c>en</c>.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithLocalizedText(PropertyId propertyId, string value, LanguageCode language) =>
        WithProperty(new(propertyId, PropertyValue.Text(value), language));

    /// <summary>Adds an <c>Integer</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The number.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithInteger(PropertyId propertyId, long value) =>
        WithProperty(new(propertyId, PropertyValue.Integer(value)));

    /// <summary>Adds a <c>Decimal</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The number.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithDecimal(PropertyId propertyId, decimal value) =>
        WithProperty(new(propertyId, PropertyValue.Decimal(value)));

    /// <summary>Adds a <c>Boolean</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The flag.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithBoolean(PropertyId propertyId, bool value) =>
        WithProperty(new(propertyId, PropertyValue.Boolean(value)));

    /// <summary>Adds a <c>Timestamp</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The timestamp.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithTimestamp(PropertyId propertyId, DateTimeOffset value) =>
        WithProperty(new(propertyId, PropertyValue.Timestamp(value)));

    /// <summary>Adds a <c>List</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="items">The elements.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithList(PropertyId propertyId, params IReadOnlyList<string> items) =>
        WithProperty(new(propertyId, PropertyValue.List(items)));

    /// <summary>Adds a <c>LocalizedList</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="items">The elements.</param>
    /// <param name="language">The language, such as <c>en</c>.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithLocalizedList(PropertyId propertyId, IReadOnlyList<string> items, LanguageCode language) =>
        WithProperty(new(propertyId, PropertyValue.List(items), language));

    /// <summary>Clears a configured property by storing an empty value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithCleared(PropertyId propertyId) =>
        WithProperty(new(propertyId, PropertyValue.Clear));

    /// <summary>Adds an already-built property value.</summary>
    /// <param name="property">The property value.</param>
    /// <returns>The builder, for chaining.</returns>
    public SourceEntryBuilder WithProperty(EntryProperty property)
    {
        _properties.Add(property);
        return this;
    }

    /// <summary>Finishes the entry.</summary>
    /// <returns>The built entry.</returns>
    public SourceEntry Build() => new(_id, [.. _properties]);

    /// <summary>Finishes the entry implicitly.</summary>
    /// <param name="builder">The builder to finish.</param>
    public static implicit operator SourceEntry(SourceEntryBuilder builder) => builder.Build();
}
