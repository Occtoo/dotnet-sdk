namespace Occtoo.Sources;

/// <summary>
/// Implemented by anything typed property values can be added to: a
/// <see cref="SourceEntryBuilder"/> for ingest, or an
/// <see cref="Occtoo.Assets.AssetUpload"/> for an asset. The typed
/// <c>With*</c> methods in <see cref="EntryPropertyBuilderExtensions"/> build
/// on <see cref="WithProperty"/>. <see cref="WithProperty"/> may return a new
/// instance instead of changing the one it was called on, as
/// <see cref="Occtoo.Assets.AssetUpload"/> does. Keep what it returns.
/// </summary>
/// <typeparam name="TSelf">The implementing type, returned for chaining.</typeparam>
public interface IEntryPropertyBuilder<TSelf>
    where TSelf : IEntryPropertyBuilder<TSelf>
{
    /// <summary>Adds an already-built property value.</summary>
    /// <param name="property">The property value.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    TSelf WithProperty(EntryProperty property);
}

/// <summary>
/// The typed <c>With*</c> methods, shared by every
/// <see cref="IEntryPropertyBuilder{TSelf}"/>.
/// </summary>
/// <remarks>
/// Every method takes the value-object types — <see cref="PropertyId"/>,
/// <see cref="LanguageCode"/> — which convert implicitly from strings through
/// their validation, throwing <see cref="Vogen.ValueObjectValidationException"/>
/// for invalid input: the same construction-time contract as the value objects
/// themselves.
/// </remarks>
public static class EntryPropertyBuilderExtensions
{
    /// <summary>Adds a <c>Text</c> value.</summary>
    /// <typeparam name="TSelf">The builder's type.</typeparam>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The text.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    public static TSelf WithText<TSelf>(this TSelf builder, PropertyId propertyId, string value)
        where TSelf : IEntryPropertyBuilder<TSelf> =>
        builder.WithProperty(new(propertyId, PropertyValue.Text(value)));

    /// <summary>Adds a <c>LocalizedText</c> value.</summary>
    /// <typeparam name="TSelf">The builder's type.</typeparam>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The text.</param>
    /// <param name="language">The language, such as <c>en</c>.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    public static TSelf WithLocalizedText<TSelf>(
        this TSelf builder,
        PropertyId propertyId,
        string value,
        LanguageCode language)
        where TSelf : IEntryPropertyBuilder<TSelf> =>
        builder.WithProperty(new(propertyId, PropertyValue.Text(value), language));

    /// <summary>Adds an <c>Integer</c> value.</summary>
    /// <typeparam name="TSelf">The builder's type.</typeparam>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The number.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    public static TSelf WithInteger<TSelf>(this TSelf builder, PropertyId propertyId, long value)
        where TSelf : IEntryPropertyBuilder<TSelf> =>
        builder.WithProperty(new(propertyId, PropertyValue.Integer(value)));

    /// <summary>Adds a <c>Decimal</c> value.</summary>
    /// <typeparam name="TSelf">The builder's type.</typeparam>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The number.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    public static TSelf WithDecimal<TSelf>(this TSelf builder, PropertyId propertyId, decimal value)
        where TSelf : IEntryPropertyBuilder<TSelf> =>
        builder.WithProperty(new(propertyId, PropertyValue.Decimal(value)));

    /// <summary>Adds a <c>Boolean</c> value.</summary>
    /// <typeparam name="TSelf">The builder's type.</typeparam>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The flag.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    public static TSelf WithBoolean<TSelf>(this TSelf builder, PropertyId propertyId, bool value)
        where TSelf : IEntryPropertyBuilder<TSelf> =>
        builder.WithProperty(new(propertyId, PropertyValue.Boolean(value)));

    /// <summary>Adds a <c>Timestamp</c> value.</summary>
    /// <typeparam name="TSelf">The builder's type.</typeparam>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="propertyId">The source property.</param>
    /// <param name="value">The timestamp.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    public static TSelf WithTimestamp<TSelf>(this TSelf builder, PropertyId propertyId, DateTimeOffset value)
        where TSelf : IEntryPropertyBuilder<TSelf> =>
        builder.WithProperty(new(propertyId, PropertyValue.Timestamp(value)));

    /// <summary>Adds a <c>List</c> value.</summary>
    /// <typeparam name="TSelf">The builder's type.</typeparam>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="propertyId">The source property.</param>
    /// <param name="items">The elements.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    public static TSelf WithList<TSelf>(this TSelf builder, PropertyId propertyId, params IReadOnlyList<string> items)
        where TSelf : IEntryPropertyBuilder<TSelf> =>
        builder.WithProperty(new(propertyId, PropertyValue.List(items)));

    /// <summary>Adds a <c>LocalizedList</c> value.</summary>
    /// <typeparam name="TSelf">The builder's type.</typeparam>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="propertyId">The source property.</param>
    /// <param name="items">The elements.</param>
    /// <param name="language">The language, such as <c>en</c>.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    public static TSelf WithLocalizedList<TSelf>(
        this TSelf builder,
        PropertyId propertyId,
        IReadOnlyList<string> items,
        LanguageCode language)
        where TSelf : IEntryPropertyBuilder<TSelf> =>
        builder.WithProperty(new(propertyId, PropertyValue.List(items), language));

    /// <summary>
    /// Adds a clear for each language listed, or one without a language for a
    /// property that isn't localized. Occtoo refuses a clear without a
    /// language on a localized property.
    /// </summary>
    /// <typeparam name="TSelf">The builder's type.</typeparam>
    /// <param name="builder">The builder to add to.</param>
    /// <param name="propertyId">The source property.</param>
    /// <param name="languages">The languages to clear, for a localized property; none otherwise.</param>
    /// <returns>
    /// The builder with the value added. Keep it — for an immutable builder
    /// such as <see cref="Occtoo.Assets.AssetUpload"/>, it's a new instance.
    /// </returns>
    public static TSelf WithCleared<TSelf>(
        this TSelf builder,
        PropertyId propertyId,
        params IReadOnlyList<LanguageCode> languages)
        where TSelf : IEntryPropertyBuilder<TSelf>
    {
        if (languages.Count == 0)
            return builder.WithProperty(new(propertyId, PropertyValue.Clear));

        var result = builder;
        foreach (var language in languages)
            result = result.WithProperty(new(propertyId, PropertyValue.Clear, language));

        return result;
    }
}
