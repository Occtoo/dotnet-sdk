namespace Occtoo.Events;

/// <summary>
/// A typed builder for the Events API's filter grammar. Filters are anchored
/// on event types, and each type only offers the conditions the API accepts
/// for it — filtering <c>card_definition.updated</c> by <c>sourceId</c> is a
/// compile error, not a <c>400</c>.
/// </summary>
/// <example>
/// <code>
/// var filter = EventFilter
///     .OfType&lt;SourceEntryAdded&gt;(e => e.WithSource("products", "assets"))
///     .OrType&lt;SourceEntryUpdated&gt;(e => e.WithSource("products"))
///     .OrType&lt;SegmentUpdated&gt;(e => e.WithSegment("summer-sale"));
///
/// // (type eq "source_entry.added" and (sourceId eq "products" or sourceId eq "assets"))
/// //   or (type eq "source_entry.updated" and sourceId eq "products")
/// //   or (type eq "segment.updated" and segmentId eq "summer-sale")
/// </code>
/// An abstract family base covers all its members:
/// <c>EventFilter.OfType&lt;SourceEntryEvent&gt;()</c> matches every
/// <c>source_entry.*</c> type.
/// </example>
public sealed class EventFilter
{
    private readonly IReadOnlyList<string> _groups;

    private EventFilter(IReadOnlyList<string> groups) => _groups = groups;

    /// <summary>
    /// Starts a filter matching one event type — or every member of a family
    /// base.
    /// </summary>
    public static EventFilter OfType<T>()
        where T : CloudEvent =>
        new([Render<T>(new EventTypeConditions<T>())]);

    /// <summary>
    /// Starts a filter matching one event type (or every member of a family
    /// base), narrowed by conditions — only those the API accepts for
    /// <typeparamref name="T"/> compile.
    /// </summary>
    public static EventFilter OfType<T>(Func<EventTypeConditions<T>, EventTypeConditions<T>> conditions)
        where T : CloudEvent =>
        new([Render(conditions(new EventTypeConditions<T>()))]);

    /// <summary>Adds another event type to match.</summary>
    public EventFilter OrType<T>()
        where T : CloudEvent =>
        new([.. _groups, Render<T>(new EventTypeConditions<T>())]);

    /// <summary>Adds another event type to match, narrowed by its own conditions.</summary>
    public EventFilter OrType<T>(Func<EventTypeConditions<T>, EventTypeConditions<T>> conditions)
        where T : CloudEvent =>
        new([.. _groups, Render(conditions(new EventTypeConditions<T>()))]);

    /// <summary>
    /// A hand-written filter in the API's grammar, sent verbatim — the escape
    /// hatch for expressions the builder does not model.
    /// </summary>
    public static EventFilter Raw(string filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        return new([filter]);
    }

    /// <summary>The filter in the API's grammar, as sent.</summary>
    public override string ToString() =>
        _groups.Count == 1 ? _groups[0] : string.Join(" or ", _groups.Select(group => $"({group})"));

    private static string Render<T>(EventTypeConditions<T> conditions)
        where T : CloudEvent
    {
        var names = EventTypeCatalog.NamesFor<T>();
        var types = names.Count == 1
            ? Comparison("type", names[0])
            : $"({string.Join(" or ", names.Select(name => Comparison("type", name)))})";

        return conditions.Clauses.Count == 0
            ? types
            : $"{types} and {string.Join(" and ", conditions.Clauses)}";
    }

    internal static string Comparison(string property, string value)
    {
        // Double quotes inside values are escaped per the SCIM-derived grammar.
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"{property} eq \"{escaped}\"";
    }
}

/// <summary>
/// The conditions being collected for one event type in an
/// <see cref="EventFilter"/>. Condition methods are extension methods gated on
/// the type's filter capabilities — see <c>EventFilterConditions</c>.
/// </summary>
/// <typeparam name="T">The event record the conditions apply to.</typeparam>
public readonly struct EventTypeConditions<T>
    where T : CloudEvent
{
    private readonly IReadOnlyList<string>? _clauses;

    private EventTypeConditions(IReadOnlyList<string> clauses) => _clauses = clauses;

    // The default instance (no clauses yet) is the valid starting point.
    internal IReadOnlyList<string> Clauses => _clauses ?? [];

    internal EventTypeConditions<T> With(string property, params IReadOnlyList<string> values)
    {
        var clause = values.Count == 1
            ? EventFilter.Comparison(property, values[0])
            : $"({string.Join(" or ", values.Select(value => EventFilter.Comparison(property, value)))})";

        return new([.. Clauses, clause]);
    }
}

/// <summary>
/// The condition vocabulary of <see cref="EventFilter"/>. Each method exists
/// only for event types whose filter capability marker admits it, so an
/// invalid combination does not compile. Passing several values combines them
/// with <c>or</c>.
/// </summary>
public static class EventFilterConditions
{
    /// <summary>Matches events for any of the given sources.</summary>
    public static EventTypeConditions<T> WithSource<T>(
        this EventTypeConditions<T> conditions, params IReadOnlyList<Sources.SourceId> sources)
        where T : CloudEvent, IFilterableBySource =>
        conditions.With("sourceId", [.. sources.Select(id => id.Value)]);

    /// <summary>Matches events for any of the given card definitions.</summary>
    public static EventTypeConditions<T> WithCardDefinition<T>(
        this EventTypeConditions<T> conditions, params IReadOnlyList<CardDefinitionId> cardDefinitions)
        where T : CloudEvent, IFilterableByCardDefinition =>
        conditions.With("cardDefinitionId", [.. cardDefinitions.Select(id => id.Value)]);

    /// <summary>Matches events for any of the given segments.</summary>
    public static EventTypeConditions<T> WithSegment<T>(
        this EventTypeConditions<T> conditions, params IReadOnlyList<SegmentId> segments)
        where T : CloudEvent, IFilterableBySegment =>
        conditions.With("segmentId", [.. segments.Select(id => id.Value)]);

    /// <summary>Matches events for any of the given segment definitions.</summary>
    public static EventTypeConditions<T> WithSegmentDefinition<T>(
        this EventTypeConditions<T> conditions, params IReadOnlyList<SegmentDefinitionId> segmentDefinitions)
        where T : CloudEvent, IFilterableBySegmentDefinition =>
        conditions.With("segmentDefinitionId", [.. segmentDefinitions.Select(id => id.Value)]);

    /// <summary>Matches events for any of the given destinations.</summary>
    public static EventTypeConditions<T> WithDestination<T>(
        this EventTypeConditions<T> conditions, params IReadOnlyList<DestinationId> destinations)
        where T : CloudEvent, IFilterableByDestination =>
        conditions.With("destinationId", [.. destinations.Select(id => id.Value)]);

    /// <summary>Matches events for any of the given endpoints.</summary>
    public static EventTypeConditions<T> WithEndpoint<T>(
        this EventTypeConditions<T> conditions, params IReadOnlyList<EndpointId> endpoints)
        where T : CloudEvent, IFilterableByEndpoint =>
        conditions.With("endpointId", [.. endpoints.Select(id => id.Value)]);

    /// <summary>Matches events for any of the given API versions, by id.</summary>
    public static EventTypeConditions<T> WithApiVersion<T>(
        this EventTypeConditions<T> conditions, params IReadOnlyList<ApiVersionId> apiVersions)
        where T : CloudEvent, IFilterableByApiVersion =>
        conditions.With("apiVersionId", [.. apiVersions.Select(id => id.Value)]);

    /// <summary>Matches events for any of the given API versions, by name.</summary>
    public static EventTypeConditions<T> WithApiVersionName<T>(
        this EventTypeConditions<T> conditions, params IReadOnlyList<string> names)
        where T : CloudEvent, IFilterableByApiVersion =>
        conditions.With("apiVersion", names);

    /// <summary>Matches events for any of the given users.</summary>
    public static EventTypeConditions<T> WithUser<T>(
        this EventTypeConditions<T> conditions, params IReadOnlyList<UserId> users)
        where T : CloudEvent, IFilterableByUser =>
        conditions.With("userId", [.. users.Select(id => id.Value)]);
}
