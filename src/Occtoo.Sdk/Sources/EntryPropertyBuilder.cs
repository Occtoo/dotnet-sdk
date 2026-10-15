namespace Occtoo.Sources;

/// <summary>
/// Collects typed property values and finishes them into a
/// <see cref="SourceEntry"/> or an <see cref="Occtoo.Assets.AssetUpload"/>.
/// Start one with <see cref="SourceEntry.WithId"/> or one of the
/// <c>AssetUpload.From*</c> methods. Converts implicitly, so a finished
/// builder can be passed wherever the result is expected without calling
/// <see cref="Build"/>.
/// </summary>
/// <remarks>
/// Every method takes the value-object types — <see cref="PropertyId"/>,
/// <see cref="LanguageCode"/> — which convert implicitly from strings through
/// their validation, throwing <see cref="Vogen.ValueObjectValidationException"/>
/// for invalid input: the same construction-time contract as the value objects
/// themselves.
/// </remarks>
/// <typeparam name="TResult">What <see cref="Build"/> finishes.</typeparam>
public sealed class EntryPropertyBuilder<TResult>
{
    private readonly Func<IReadOnlyList<EntryProperty>, TResult> _create;
    private readonly List<EntryProperty> _properties = [];

    internal EntryPropertyBuilder(Func<IReadOnlyList<EntryProperty>, TResult> create) => _create = create;

    /// <summary>Adds a <c>Text</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The text.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithText(PropertyId propertyId, string value) =>
        WithProperty(new(propertyId, PropertyValue.Text(value)));

    /// <summary>Adds a <c>LocalizedText</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The text.</param>
    /// <param name="language">The language, such as <c>en</c>.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithLocalizedText(PropertyId propertyId, string value, LanguageCode language) =>
        WithProperty(new(propertyId, PropertyValue.Text(value), language));

    /// <summary>Adds an <c>Integer</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The number.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithInteger(PropertyId propertyId, long value) =>
        WithProperty(new(propertyId, PropertyValue.Integer(value)));

    /// <summary>Adds a <c>Decimal</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The number.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithDecimal(PropertyId propertyId, decimal value) =>
        WithProperty(new(propertyId, PropertyValue.Decimal(value)));

    /// <summary>Adds a <c>Boolean</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The flag.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithBoolean(PropertyId propertyId, bool value) =>
        WithProperty(new(propertyId, PropertyValue.Boolean(value)));

    /// <summary>Adds a <c>Timestamp</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The timestamp.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithTimestamp(PropertyId propertyId, DateTimeOffset value) =>
        WithProperty(new(propertyId, PropertyValue.Timestamp(value)));

    /// <summary>Adds a <c>List</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="items">The elements.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithList(PropertyId propertyId, params IReadOnlyList<string> items) =>
        WithProperty(new(propertyId, PropertyValue.List(items)));

    /// <summary>Adds a <c>LocalizedList</c> value.</summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="items">The elements.</param>
    /// <param name="language">The language, such as <c>en</c>.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithLocalizedList(
        PropertyId propertyId,
        IReadOnlyList<string> items,
        LanguageCode language) =>
        WithProperty(new(propertyId, PropertyValue.List(items), language));

    /// <summary>
    /// Adds a clear for each language listed, or one without a language for a
    /// property that isn't localized. Occtoo refuses a clear without a
    /// language on a localized property.
    /// </summary>
    /// <param name="propertyId">The source property.</param>
    /// <param name="languages">The languages to clear, for a localized property; none otherwise.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithCleared(
        PropertyId propertyId,
        params IReadOnlyList<LanguageCode> languages)
    {
        if (languages.Count == 0)
            return WithProperty(new(propertyId, PropertyValue.Clear));

        _properties.AddRange(languages.Select(language => new EntryProperty(propertyId, PropertyValue.Clear, language)));
        return this;
    }

    /// <summary>Adds an already-built property value.</summary>
    /// <param name="property">The property value.</param>
    /// <returns>The builder, for chaining.</returns>
    public EntryPropertyBuilder<TResult> WithProperty(EntryProperty property)
    {
        _properties.Add(property);
        return this;
    }

    /// <summary>Finishes the result with the values added so far.</summary>
    /// <returns>The built result.</returns>
    public TResult Build() => _create([.. _properties]);

    /// <summary>Finishes the result implicitly.</summary>
    /// <param name="builder">The builder to finish.</param>
    public static implicit operator TResult(EntryPropertyBuilder<TResult> builder) => builder.Build();
}