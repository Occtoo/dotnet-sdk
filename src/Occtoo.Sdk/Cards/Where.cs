using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Occtoo.Events;
using Occtoo.Sources;

namespace Occtoo.Cards;

/// <summary>
/// A condition on card entries — the <c>where</c> that segments, card exports
/// and card entry search take. Each leaf allows exactly one operation, so a
/// condition the API would reject for having none or several doesn't compile.
/// </summary>
/// <example>
/// <code>
/// var inStock = Where.Text("brand").IgnoringCase().Eq("acme")
///     &amp; Where.Integer("stock").Gt(0).Lte(100)
///     &amp; !Where.InSegment("campaigns", "clearance");
/// </code>
/// </example>
public abstract class Where
{
    private protected Where()
    {
    }

    /// <summary>A condition on a text card property.</summary>
    public static TextCondition Text(CardPropertyId property) => new(property, false, []);

    /// <summary>A condition on an integer card property.</summary>
    [SuppressMessage("Naming", "CA1720", Justification = "Named after the API's predicate branch.")]
    public static OrderedCondition<long> Integer(CardPropertyId property) => new("integer", property);

    /// <summary>A condition on a decimal card property.</summary>
    [SuppressMessage("Naming", "CA1720", Justification = "Named after the API's predicate branch.")]
    public static OrderedCondition<decimal> Decimal(CardPropertyId property) => new("decimal", property);

    /// <summary>A condition on a timestamp card property.</summary>
    public static OrderedCondition<DateTimeOffset> Timestamp(CardPropertyId property) => new("timestamp", property);

    /// <summary>A condition on a boolean card property.</summary>
    public static BooleanCondition Boolean(CardPropertyId property) => new(property);

    /// <summary>A condition on the elements or length of a list card property.</summary>
    public static ListCondition List(CardPropertyId property) => new(property, false, []);

    /// <summary>
    /// The property has a value — empty text, whitespace and an empty list
    /// count as values. With languages, each of them must have one.
    /// </summary>
    public static Where HasValue(CardPropertyId property, params IReadOnlyList<LanguageCode> languages)
    {
        CheckLanguages(languages);
        return new Leaf("hasValue", writer =>
        {
            writer.WriteString("property", property.Value);
            Languages(writer, languages);
        });
    }

    /// <summary>The card is a member of a segment.</summary>
    public static Where InSegment(SegmentDefinitionId definitionId, SegmentId segmentId) =>
        new Leaf("segment", writer =>
        {
            writer.WriteString("definitionId", definitionId.Value);
            writer.WriteString("id", segmentId.Value);
        });

    /// <summary>
    /// The card has live related cards through a relation definition — narrow
    /// it with <see cref="RelationCondition.Matching"/> and
    /// <see cref="RelationCondition.Counting"/>.
    /// </summary>
    public static RelationCondition Related(RelationDefinitionId relation) => new(relation, null, null);

    /// <summary>Every condition matches.</summary>
    public static Where All(params IReadOnlyList<Where> conditions) => new Group("and", Flatten("and", conditions));

    /// <summary>At least one condition matches.</summary>
    public static Where Any(params IReadOnlyList<Where> conditions) => new Group("or", Flatten("or", conditions));

    /// <summary>The condition does not match.</summary>
    public static Where Not(Where condition) => new Negation(condition ?? throw new ArgumentNullException(nameof(condition)));

    /// <summary>
    /// A condition in the API's JSON form, sent as it is — the escape hatch
    /// for what this builder doesn't model, such as lifecycle readiness.
    /// </summary>
    public static Where Raw(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? new RawCondition(document.RootElement.Clone())
            : throw new ArgumentException("A condition is a JSON object.", nameof(json));
    }

    /// <summary>Both conditions match.</summary>
    public static Where operator &(Where left, Where right) => All(left, right);

    /// <summary>Either condition matches.</summary>
    public static Where operator |(Where left, Where right) => Any(left, right);

    /// <summary>The condition does not match.</summary>
    public static Where operator !(Where condition) => Not(condition);

    /// <summary>Both conditions match.</summary>
    public static Where BitwiseAnd(Where left, Where right) => All(left, right);

    /// <summary>Either condition matches.</summary>
    public static Where BitwiseOr(Where left, Where right) => Any(left, right);

    /// <summary>The condition does not match.</summary>
    public static Where LogicalNot(Where condition) => Not(condition);

    /// <summary>The condition in the API's JSON form.</summary>
    public override string ToString()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            Write(writer);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal abstract void Write(Utf8JsonWriter writer);

    internal static IReadOnlyList<LanguageCode> CheckLanguages(IReadOnlyList<LanguageCode> languages) =>
        languages is null or { Count: 0 }
        || (languages.Count <= 64 && languages.Select(language => language.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() == languages.Count)
            ? languages ?? []
            : throw new ArgumentException("Languages must be distinct, at most 64.", nameof(languages));

    internal static void Languages(Utf8JsonWriter writer, IReadOnlyList<LanguageCode> languages)
    {
        if (languages is { Count: 0 })
            return;

        writer.WriteStartArray("languages");
        foreach (var language in languages)
            writer.WriteStringValue(language.Value);
        writer.WriteEndArray();
    }

    internal static IReadOnlyList<T> NonEmpty<T>(IReadOnlyList<T> values, string name) =>
        values is { Count: > 0 } ? values : throw new ArgumentException($"At least one {name} is required.", name);

    private static List<Where> Flatten(string branch, IReadOnlyList<Where> conditions)
    {
        var flattened = new List<Where>();
        foreach (var condition in NonEmpty(conditions, "condition"))
        {
            if (condition is Group group && group.Branch == branch)
                flattened.AddRange(group.Conditions);
            else
                flattened.Add(condition ?? throw new ArgumentException("A condition must not be null.", nameof(conditions)));
        }

        return flattened;
    }

    // Reads a condition as Occtoo returns it, such as a segment's stored
    // where. A branch the builder doesn't model is kept verbatim, so it
    // round-trips unchanged.
    internal static Where Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("A condition is a JSON object.");

        foreach (var branch in element.EnumerateObject())
        {
            switch (branch.Name)
            {
                case "and" or "or":
                    return new Group(branch.Name, [.. branch.Value.EnumerateArray().Select(Read)]);
                case "not":
                    return new Negation(Read(branch.Value));
                case "relation":
                    return new RelationCondition(
                        RelationDefinitionId.Read(branch.Value.GetProperty("id").GetString()!),
                        branch.Value.TryGetProperty("where", out var where) && where.ValueKind == JsonValueKind.Object ? Read(where) : null,
                        branch.Value.TryGetProperty("count", out var count) && count.ValueKind == JsonValueKind.Object ? new RawCount(count.Clone()) : null);
            }
        }

        // Leaves (text, integer, list, segment, …) carry nothing the builder
        // needs to reinterpret, so they are kept as they came.
        return new RawCondition(element.Clone());
    }

    internal static void Value<T>(Utf8JsonWriter writer, string name, T value)
    {
        switch (value)
        {
            case long number:
                writer.WriteNumber(name, number);
                break;
            case decimal number:
                writer.WriteNumber(name, number);
                break;
            case DateTimeOffset time:
                writer.WriteString(name, time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                break;
            case bool flag:
                writer.WriteBoolean(name, flag);
                break;
            default:
                writer.WriteString(name, value?.ToString());
                break;
        }
    }

    internal static void Values<T>(Utf8JsonWriter writer, string name, IReadOnlyList<T> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            switch (value)
            {
                case long number:
                    writer.WriteNumberValue(number);
                    break;
                case decimal number:
                    writer.WriteNumberValue(number);
                    break;
                case DateTimeOffset time:
                    writer.WriteStringValue(time.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                    break;
                default:
                    writer.WriteStringValue(value?.ToString());
                    break;
            }
        }

        writer.WriteEndArray();
    }

    private sealed class Group(string branch, IReadOnlyList<Where> conditions) : Where
    {
        internal string Branch { get; } = branch;

        internal IReadOnlyList<Where> Conditions { get; } = conditions;

        internal override void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteStartArray(Branch);
            foreach (var condition in Conditions)
                condition.Write(writer);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    private sealed class Negation(Where condition) : Where
    {
        internal override void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("not");
            condition.Write(writer);
            writer.WriteEndObject();
        }
    }

    private sealed class RawCondition(JsonElement json) : Where
    {
        internal override void Write(Utf8JsonWriter writer) => json.WriteTo(writer);
    }

    private sealed class RawCount(JsonElement json) : CountRange(null, null, null)
    {
        internal override void Write(Utf8JsonWriter writer, string name)
        {
            writer.WritePropertyName(name);
            json.WriteTo(writer);
        }
    }

    internal sealed class Leaf(string branch, Action<Utf8JsonWriter> body) : Where
    {
        internal override void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteStartObject(branch);
            body(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }
}

/// <summary>
/// Bounds on a count — a text's length in Unicode code points, a list's
/// length, or a number of related cards. Bounds are inclusive.
/// </summary>
public class CountRange
{
    private readonly long? _eq;
    private readonly long? _gte;
    private readonly long? _lte;

    private protected CountRange(long? eq, long? gte, long? lte)
    {
        _eq = eq;
        _gte = gte;
        _lte = lte;
    }

    /// <summary>Exactly <paramref name="count"/>.</summary>
    public static CountRange Exactly(long count) => new(NonNegative(count), null, null);

    /// <summary>At least <paramref name="count"/>.</summary>
    public static CountRange AtLeast(long count) => new(null, NonNegative(count), null);

    /// <summary>At most <paramref name="count"/>.</summary>
    public static CountRange AtMost(long count) => new(null, null, NonNegative(count));

    /// <summary>From <paramref name="minimum"/> to <paramref name="maximum"/>, both included.</summary>
    public static CountRange Between(long minimum, long maximum) =>
        NonNegative(minimum) <= NonNegative(maximum)
            ? new(null, minimum, maximum)
            : throw new ArgumentException("The minimum must not exceed the maximum.", nameof(minimum));

    internal virtual void Write(Utf8JsonWriter writer, string name)
    {
        writer.WriteStartObject(name);
        if (_eq is { } eq)
            writer.WriteNumber("eq", eq);
        if (_gte is { } gte)
            writer.WriteNumber("gte", gte);
        if (_lte is { } lte)
            writer.WriteNumber("lte", lte);
        writer.WriteEndObject();
    }

    private static long NonNegative(long count) =>
        count >= 0 ? count : throw new ArgumentOutOfRangeException(nameof(count), "A count is never negative.");
}

/// <summary>A condition on a text card property; pick one operation.</summary>
public sealed class TextCondition
{
    private readonly CardPropertyId _property;
    private readonly bool _ignoreCase;
    private readonly IReadOnlyList<LanguageCode> _languages;

    internal TextCondition(CardPropertyId property, bool ignoreCase, IReadOnlyList<LanguageCode> languages)
    {
        _property = property;
        _ignoreCase = ignoreCase;
        _languages = languages;
    }

    /// <summary>Matches regardless of case.</summary>
    public TextCondition IgnoringCase() => new(_property, true, _languages);

    /// <summary>Every listed translation must match; there is no language fallback.</summary>
    public TextCondition InLanguages(params IReadOnlyList<LanguageCode> languages) =>
        new(_property, _ignoreCase, Where.CheckLanguages(Where.NonEmpty(languages, "language")));

    /// <summary>Equal to the value.</summary>
    public Where Eq(string value) => Leaf(writer => writer.WriteString("eq", value));

    /// <summary>Different from the value.</summary>
    public Where Neq(string value) => Leaf(writer => writer.WriteString("neq", value));

    /// <summary>Equal to any of the values.</summary>
    public Where In(params IReadOnlyList<string> values)
    {
        Where.NonEmpty(values, "value");
        return Leaf(writer => Where.Values(writer, "in", values));
    }

    /// <summary>Contains the literal substring.</summary>
    public Where Contains(string value) => Leaf(writer => writer.WriteString("contains", value));

    /// <summary>The whole value matches a wildcard pattern of <c>*</c> and <c>?</c>.</summary>
    public Where Matches(string pattern) => Leaf(writer => writer.WriteString("pattern", pattern));

    /// <summary>A substring matches the RE2 regular expression.</summary>
    public Where Regex(string expression) => Leaf(writer => writer.WriteString("regex", expression));

    /// <summary>The length, in Unicode code points, is within the range.</summary>
    public Where Length(CountRange range) => Leaf(writer => range.Write(writer, "length"));

    private Where.Leaf Leaf(Action<Utf8JsonWriter> operation) => new("text", writer =>
    {
        writer.WriteString("property", _property.Value);
        operation(writer);
        if (_ignoreCase)
            writer.WriteBoolean("ignoreCase", true);
        Where.Languages(writer, _languages);
    });
}

/// <summary>A condition on a list card property; pick one operation.</summary>
public sealed class ListCondition
{
    private readonly CardPropertyId _property;
    private readonly bool _ignoreCase;
    private readonly IReadOnlyList<LanguageCode> _languages;

    internal ListCondition(CardPropertyId property, bool ignoreCase, IReadOnlyList<LanguageCode> languages)
    {
        _property = property;
        _ignoreCase = ignoreCase;
        _languages = languages;
    }

    /// <summary>Matches elements regardless of case.</summary>
    public ListCondition IgnoringCase() => new(_property, true, _languages);

    /// <summary>Every listed translation must match; there is no language fallback.</summary>
    public ListCondition InLanguages(params IReadOnlyList<LanguageCode> languages) =>
        new(_property, _ignoreCase, Where.CheckLanguages(Where.NonEmpty(languages, "language")));

    /// <summary>One element equals the value.</summary>
    public Where Contains(string value) => Leaf(writer => writer.WriteString("contains", value));

    /// <summary>One element matches a wildcard pattern of <c>*</c> and <c>?</c>.</summary>
    public Where Matches(string pattern) => Leaf(writer => writer.WriteString("pattern", pattern));

    /// <summary>Every element is one of the values; an empty or missing list doesn't match.</summary>
    public Where Only(params IReadOnlyList<string> values)
    {
        Where.NonEmpty(values, "value");
        return Leaf(writer => Where.Values(writer, "only", values));
    }

    /// <summary>The number of elements, duplicates and empty ones included, is within the range.</summary>
    public Where Length(CountRange range) => Leaf(writer => range.Write(writer, "length"));

    private Where.Leaf Leaf(Action<Utf8JsonWriter> operation) => new("list", writer =>
    {
        writer.WriteString("property", _property.Value);
        operation(writer);
        if (_ignoreCase)
            writer.WriteBoolean("ignoreCase", true);
        Where.Languages(writer, _languages);
    });
}

/// <summary>A condition on a boolean card property.</summary>
public sealed class BooleanCondition
{
    private readonly CardPropertyId _property;

    internal BooleanCondition(CardPropertyId property) => _property = property;

    /// <summary>Equal to the value.</summary>
    public Where Eq(bool value) => Leaf(writer => writer.WriteBoolean("eq", value));

    /// <summary>Different from the value.</summary>
    public Where Neq(bool value) => Leaf(writer => writer.WriteBoolean("neq", value));

    private Where.Leaf Leaf(Action<Utf8JsonWriter> operation) => new("boolean", writer =>
    {
        writer.WriteString("property", _property.Value);
        operation(writer);
    });
}

/// <summary>
/// A condition on an integer, decimal or timestamp card property: one
/// equality, or a range of one lower and one upper bound.
/// </summary>
public sealed class OrderedCondition<T>
    where T : struct, IComparable<T>
{
    private readonly string _branch;
    private readonly CardPropertyId _property;

    internal OrderedCondition(string branch, CardPropertyId property)
    {
        _branch = branch;
        _property = property;
    }

    /// <summary>Equal to the value.</summary>
    public Where Eq(T value) => Leaf(writer => Where.Value(writer, "eq", value));

    /// <summary>Different from the value.</summary>
    public Where Neq(T value) => Leaf(writer => Where.Value(writer, "neq", value));

    /// <summary>Equal to any of the values.</summary>
    public Where In(params IReadOnlyList<T> values)
    {
        Where.NonEmpty(values, "value");
        return Leaf(writer => Where.Values(writer, "in", values));
    }

    /// <summary>Greater than the value; add an upper bound with <c>Lt</c> or <c>Lte</c>.</summary>
    public LowerBounded<T> Gt(T value) => new(_branch, _property, "gt", value);

    /// <summary>Greater than or equal to the value; add an upper bound with <c>Lt</c> or <c>Lte</c>.</summary>
    public LowerBounded<T> Gte(T value) => new(_branch, _property, "gte", value);

    /// <summary>Less than the value; add a lower bound with <c>Gt</c> or <c>Gte</c>.</summary>
    public UpperBounded<T> Lt(T value) => new(_branch, _property, "lt", value);

    /// <summary>Less than or equal to the value; add a lower bound with <c>Gt</c> or <c>Gte</c>.</summary>
    public UpperBounded<T> Lte(T value) => new(_branch, _property, "lte", value);

    private Where.Leaf Leaf(Action<Utf8JsonWriter> operation) => new(_branch, writer =>
    {
        writer.WriteString("property", _property.Value);
        operation(writer);
    });
}

/// <summary>A range with a lower bound, which may take an upper one.</summary>
public sealed class LowerBounded<T> : Where
    where T : struct, IComparable<T>
{
    private readonly string _branch;
    private readonly CardPropertyId _property;
    private readonly string _bound;
    private readonly T _value;

    internal LowerBounded(string branch, CardPropertyId property, string bound, T value)
    {
        _branch = branch;
        _property = property;
        _bound = bound;
        _value = value;
    }

    /// <summary>Also less than the value.</summary>
    public Where Lt(T value) => Range("lt", value, value.CompareTo(_value) > 0);

    /// <summary>Also less than or equal to the value.</summary>
    public Where Lte(T value) => Range("lte", value, _bound == "gte" ? value.CompareTo(_value) >= 0 : value.CompareTo(_value) > 0);

    internal override void Write(Utf8JsonWriter writer) =>
        new Leaf(_branch, body =>
        {
            body.WriteString("property", _property.Value);
            Value(body, _bound, _value);
        }).Write(writer);

    private Leaf Range(string bound, T value, bool nonEmpty) =>
        nonEmpty
            ? new Leaf(_branch, body =>
            {
                body.WriteString("property", _property.Value);
                Value(body, _bound, _value);
                Value(body, bound, value);
            })
            : throw new ArgumentException("The bounds describe an empty range.", nameof(value));
}

/// <summary>A range with an upper bound, which may take a lower one.</summary>
public sealed class UpperBounded<T> : Where
    where T : struct, IComparable<T>
{
    private readonly string _branch;
    private readonly CardPropertyId _property;
    private readonly string _bound;
    private readonly T _value;

    internal UpperBounded(string branch, CardPropertyId property, string bound, T value)
    {
        _branch = branch;
        _property = property;
        _bound = bound;
        _value = value;
    }

    /// <summary>Also greater than the value.</summary>
    public Where Gt(T value) => Range("gt", value, value.CompareTo(_value) < 0);

    /// <summary>Also greater than or equal to the value.</summary>
    public Where Gte(T value) => Range("gte", value, _bound == "lte" ? value.CompareTo(_value) <= 0 : value.CompareTo(_value) < 0);

    internal override void Write(Utf8JsonWriter writer) =>
        new Leaf(_branch, body =>
        {
            body.WriteString("property", _property.Value);
            Value(body, _bound, _value);
        }).Write(writer);

    private Leaf Range(string bound, T value, bool nonEmpty) =>
        nonEmpty
            ? new Leaf(_branch, body =>
            {
                body.WriteString("property", _property.Value);
                Value(body, bound, value);
                Value(body, _bound, _value);
            })
            : throw new ArgumentException("The bounds describe an empty range.", nameof(value));
}

/// <summary>
/// The card has live related cards. All nested conditions apply to the same
/// related card.
/// </summary>
public sealed class RelationCondition : Where
{
    private readonly RelationDefinitionId _relation;
    private readonly Where? _where;
    private readonly CountRange? _count;

    internal RelationCondition(RelationDefinitionId relation, Where? where, CountRange? count)
    {
        _relation = relation;
        _where = where;
        _count = count;
    }

    /// <summary>Only related cards that match the condition count.</summary>
    public RelationCondition Matching(Where where) =>
        new(_relation, where ?? throw new ArgumentNullException(nameof(where)), _count);

    /// <summary>The number of distinct matching related cards is within the range.</summary>
    public RelationCondition Counting(CountRange count) =>
        new(_relation, _where, count ?? throw new ArgumentNullException(nameof(count)));

    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteStartObject("relation");
        writer.WriteString("id", _relation.Value);
        if (_where is not null)
        {
            writer.WritePropertyName("where");
            _where.Write(writer);
        }

        _count?.Write(writer, "count");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
