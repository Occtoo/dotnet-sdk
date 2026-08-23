namespace Occtoo.Sources;

/// <summary>
/// A typed value for a source property — the closed set of shapes the typed
/// ingest endpoint accepts.
/// </summary>
/// <remarks>
/// <para>
/// Each case serializes to its native JSON type, and Occtoo validates it against
/// the source property's configured type (<c>Text</c>, <c>Integer</c>,
/// <c>Boolean</c>, ...). For properties the source does not know yet, the type
/// is inferred from the value.
/// </para>
/// <para>
/// Build values with the factory methods, or lean on the implicit conversions:
/// <c>new EntryProperty(id, "Blue chair")</c>, <c>new EntryProperty(id, 100.5m)</c>.
/// Branch with a switch expression when inspecting one.
/// </para>
/// </remarks>
public abstract record PropertyValue
{
    private PropertyValue()
    {
    }

    /// <summary>A text value, for <c>Text</c> and <c>LocalizedText</c> properties.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The value.</returns>
    public static PropertyValue Text(string value) => new TextValue(value);

    /// <summary>A whole number, for <c>Integer</c> properties.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "The factory mirrors Occtoo's own property type name.")]
    public static PropertyValue Integer(long value) => new IntegerValue(value);

    /// <summary>A decimal number, for <c>Decimal</c> properties.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
        Justification = "The factory mirrors Occtoo's own property type name.")]
    public static PropertyValue Decimal(decimal value) => new DecimalValue(value);

    /// <summary>A boolean, for <c>Boolean</c> properties.</summary>
    /// <param name="value">The flag.</param>
    /// <returns>The value.</returns>
    public static PropertyValue Boolean(bool value) => new BooleanValue(value);

    /// <summary>
    /// A point in time, for <c>Timestamp</c> properties. Serialized as an
    /// ISO 8601 string.
    /// </summary>
    /// <param name="value">The timestamp.</param>
    /// <returns>The value.</returns>
    public static PropertyValue Timestamp(DateTimeOffset value) => new TimestampValue(value);

    /// <summary>
    /// A list of strings, for <c>List</c> and <c>LocalizedList</c> properties.
    /// Elements must not contain the property's configured delimiter.
    /// </summary>
    /// <param name="items">The elements.</param>
    /// <returns>The value.</returns>
    public static PropertyValue List(params IReadOnlyList<string> items) => new ListValue(items);

    /// <summary>
    /// Clears a configured property by storing an empty value. Serialized as
    /// JSON <c>null</c>. Occtoo cannot infer a type from this, so it only
    /// applies to properties the source already knows.
    /// </summary>
    public static PropertyValue Clear { get; } = new ClearValue();

    /// <summary>A text value.</summary>
    /// <param name="Value">The text.</param>
    public sealed record TextValue(string Value) : PropertyValue;

    /// <summary>A whole number.</summary>
    /// <param name="Value">The number.</param>
    public sealed record IntegerValue(long Value) : PropertyValue;

    /// <summary>A decimal number.</summary>
    /// <param name="Value">The number.</param>
    public sealed record DecimalValue(decimal Value) : PropertyValue;

    /// <summary>A boolean.</summary>
    /// <param name="Value">The flag.</param>
    public sealed record BooleanValue(bool Value) : PropertyValue;

    /// <summary>A point in time.</summary>
    /// <param name="Value">The timestamp.</param>
    public sealed record TimestampValue(DateTimeOffset Value) : PropertyValue;

    /// <summary>A list of strings.</summary>
    /// <param name="Items">The elements.</param>
    public sealed record ListValue(IReadOnlyList<string> Items) : PropertyValue;

    /// <summary>An empty value that clears the property.</summary>
    public sealed record ClearValue : PropertyValue
    {
        internal ClearValue()
        {
        }
    }

    /// <summary>Converts a string to a <see cref="TextValue"/>.</summary>
    /// <param name="value">The text.</param>
    public static implicit operator PropertyValue(string value) => Text(value);

    /// <summary>Converts a long to an <see cref="IntegerValue"/>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator PropertyValue(long value) => Integer(value);

    /// <summary>Converts an int to an <see cref="IntegerValue"/>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator PropertyValue(int value) => Integer(value);

    /// <summary>Converts a decimal to a <see cref="DecimalValue"/>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator PropertyValue(decimal value) => Decimal(value);

    /// <summary>Converts a bool to a <see cref="BooleanValue"/>.</summary>
    /// <param name="value">The flag.</param>
    public static implicit operator PropertyValue(bool value) => Boolean(value);

    /// <summary>Converts a timestamp to a <see cref="TimestampValue"/>.</summary>
    /// <param name="value">The timestamp.</param>
    public static implicit operator PropertyValue(DateTimeOffset value) => Timestamp(value);

    /// <summary>Converts a string array to a <see cref="ListValue"/>.</summary>
    /// <param name="items">The elements.</param>
    public static implicit operator PropertyValue(string[] items) => List(items);
}
