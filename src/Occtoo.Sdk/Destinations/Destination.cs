using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.Sources;
using Vogen;

namespace Occtoo.Destinations;

/// <summary>A destination's type, as Studio offers it. Studio keeps it as a tag.</summary>
public enum DestinationType
{
    /// <summary>Real-time updates, fetched dynamically.</summary>
    Dynamic,

    /// <summary>Many consumers. Not creatable from the SDK yet: it needs clusters the API doesn't take.</summary>
    MultiConsumer,

    /// <summary>A continuous feed to another system — marketplaces, data feeds.</summary>
    Static,

    /// <summary>Large-scale integrations with high availability and security needs.</summary>
    Enterprise,

    /// <summary>A Partner Zone destination; Studio doesn't create these.</summary>
    PartnerZone,
}

/// <summary>Whether callers of a published destination API need credentials. Fixed by the first version.</summary>
public enum DestinationAuthentication
{
    /// <summary>
    /// Callers send a token issued to one of the version's applications, with
    /// the version's <see cref="DestinationVersion.Id"/> as audience, or a tenant API key.
    /// </summary>
    Protected,

    /// <summary>Callers need no credentials.</summary>
    Public,
}

/// <summary>Where a destination version is in its life.</summary>
public enum DestinationVersionState
{
    /// <summary>Never published.</summary>
    New,

    /// <summary>A preview publish is running.</summary>
    PreviewPublishStarted,

    /// <summary>Published as a preview.</summary>
    Preview,

    /// <summary>A publish is running.</summary>
    PublishStarted,

    /// <summary>Published.</summary>
    Published,

    /// <summary>Being deleted.</summary>
    DeleteStarted,

    /// <summary>Deleted.</summary>
    Deleted,

    /// <summary>Stopped; <see cref="DestinationVersion.StopReasons"/> says why.</summary>
    Stopped,

    /// <summary>The last publish failed.</summary>
    PublishFailed,

    /// <summary>Publishing changed content.</summary>
    Updating,

    /// <summary>Running.</summary>
    Operational,
}

/// <summary>How far a publish is.</summary>
public enum PublicationStatus
{
    /// <summary>The version was never published.</summary>
    NeverStarted,

    /// <summary>Running.</summary>
    Running,

    /// <summary>Done.</summary>
    Completed,

    /// <summary>Failed.</summary>
    Failed,
}

/// <summary>A destination's display name: 3 to 50 characters.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct DestinationName
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator DestinationName(string value) => From(value);

    private static Validation Validate(string input) =>
        input is { Length: >= 3 and <= 50 } && !string.IsNullOrWhiteSpace(input)
            ? Validation.Ok
            : Validation.Invalid("A destination name must be 3 to 50 characters.");
}

/// <summary>A destination endpoint's display name: 3 to 50 characters.</summary>
[ValueObject<string>(toPrimitiveCasting: CastOperator.Explicit, fromPrimitiveCasting: CastOperator.None)]
public readonly partial struct EndpointName
{
    /// <summary>Converts a string through the same validation as <see cref="From"/>.</summary>
    /// <exception cref="ValueObjectValidationException">The value is invalid.</exception>
    public static implicit operator EndpointName(string value) => From(value);

    private static Validation Validate(string input) =>
        input is { Length: >= 3 and <= 50 } && !string.IsNullOrWhiteSpace(input)
            ? Validation.Ok
            : Validation.Invalid("An endpoint name must be 3 to 50 characters.");
}

/// <summary>
/// A destination: the API a destination's versions publish. <c>Type</c> is
/// read from its tags, absent when none names a Studio type.
/// </summary>
public sealed record Destination(
    DestinationId Id,
    string Name,
    Maybe<string> Description,
    IReadOnlyList<string> Tags,
    Maybe<DestinationType> Type,
    ETag ETag,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Starts a change based on this version of the destination.</summary>
    public UpdateDestination Edit() => new(Id, ETag, Type);
}

/// <summary>A language a published API serves, and the language of the card data served for it.</summary>
public sealed record DestinationVersionLanguage(LanguageCode Language, LanguageCode SourceLanguage);

/// <summary>
/// A version of a destination. Its <c>Id</c> is the audience of tokens for a
/// protected version — see the destination authentication guide.
/// </summary>
public sealed record DestinationVersion(
    DestinationId DestinationId,
    int Version,
    ApiVersionId Id,
    Maybe<DestinationVersionState> State,
    int Revision,
    Maybe<DestinationAuthentication> Authentication,
    IReadOnlyList<string> AllowedOrigins,
    IReadOnlyList<DestinationVersionLanguage> Languages,
    Maybe<LanguageCode> DefaultLanguage,
    Maybe<string> Description,
    IReadOnlyList<string> StopReasons,
    ETag ETag,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Starts a change based on this version.</summary>
    public UpdateDestinationVersion Edit() => new(DestinationId, Version, ETag);
}

/// <summary>A destination with the first version Studio creates with it.</summary>
public sealed record CreatedDestination(Destination Destination, DestinationVersion Version);

/// <summary>One step of a publish: what it works on, its title, its status and attempts.</summary>
public sealed record DestinationPublicationStep(string Type, string Title, Maybe<PublicationStatus> Status, int Attempt, DateTimeOffset Timestamp);

/// <summary>How far the latest publish of a version is, and the steps it ran.</summary>
public sealed record DestinationPublication(
    Maybe<PublicationStatus> Status,
    Maybe<DateTimeOffset> StartedAt,
    Maybe<DateTimeOffset> UpdatedAt,
    IReadOnlyList<DestinationPublicationStep> Steps);

/// <summary>
/// An endpoint of a destination version. <c>Content</c> is what the
/// published endpoint returns; <c>PendingContent</c> waits for the next publish.
/// </summary>
public sealed record DestinationEndpoint(
    DestinationId DestinationId,
    int Version,
    EndpointId Id,
    string Name,
    Maybe<string> Description,
    EndpointContent Content,
    Maybe<EndpointContent> PendingContent,
    ETag ETag)
{
    /// <summary>Starts a change based on this version of the endpoint.</summary>
    public UpdateDestinationEndpoint Edit() => new(DestinationId, Version, Id, ETag);
}

/// <summary>
/// A new destination with its first version, the way Studio creates one:
/// pick the type, then the version's authentication and languages.
/// </summary>
public sealed record CreateDestination
{
    private CreateDestination(DestinationId id, DestinationName name, DestinationType type)
    {
        Id = id;
        Name = name;
        Type = type;
    }

    internal DestinationId Id { get; }

    internal DestinationName Name { get; }

    internal DestinationType Type { get; }

    internal Maybe<string> Description { get; private init; }

    internal IReadOnlyList<string> Tags { get; private init; } = [];

    internal DestinationAuthentication Authentication { get; private init; } = DestinationAuthentication.Protected;

    internal IReadOnlyList<DestinationVersionLanguage> Languages { get; private init; } = [];

    internal Maybe<LanguageCode> DefaultLanguage { get; private init; }

    internal IReadOnlyList<string> AllowedOrigins { get; private init; } = ["*"];

    /// <summary>A Static destination: a continuous feed to another system.</summary>
    public static CreateDestination Static(DestinationId id, DestinationName name) => new(id, name, DestinationType.Static);

    /// <summary>A Dynamic destination: real-time updates.</summary>
    public static CreateDestination Dynamic(DestinationId id, DestinationName name) => new(id, name, DestinationType.Dynamic);

    /// <summary>An Enterprise destination: large-scale, highly available integrations.</summary>
    public static CreateDestination Enterprise(DestinationId id, DestinationName name) => new(id, name, DestinationType.Enterprise);

    /// <summary>Free text, up to 200 characters.</summary>
    public CreateDestination Described(string description) => this with { Description = description };

    /// <summary>More labels, besides the type's.</summary>
    public CreateDestination Tagged(params IReadOnlyList<string> tags) => this with { Tags = tags };

    /// <summary>Callers need a token or API key — the default.</summary>
    public CreateDestination Protected() => this with { Authentication = DestinationAuthentication.Protected };

    /// <summary>Callers need no credentials.</summary>
    public CreateDestination Public() => this with { Authentication = DestinationAuthentication.Public };

    /// <summary>
    /// A language the API serves, from card data in <paramref name="sourceLanguage"/>
    /// — the same language when left out. The first one is the default unless
    /// <see cref="WithDefaultLanguage"/> says otherwise.
    /// </summary>
    public CreateDestination WithLanguage(LanguageCode language, Maybe<LanguageCode> sourceLanguage = default) =>
        this with { Languages = [.. Languages, new DestinationVersionLanguage(language, sourceLanguage.GetValueOrDefault(language))] };

    /// <summary>The language used when a caller sends none; one of the languages.</summary>
    public CreateDestination WithDefaultLanguage(LanguageCode language) => this with { DefaultLanguage = language };

    /// <summary>The origins browsers may call the API from; any (<c>*</c>) by default.</summary>
    public CreateDestination AllowOrigins(params IReadOnlyList<string> origins) => this with { AllowedOrigins = origins };
}

/// <summary>Changes to a destination, from <see cref="Destination.Edit"/>.</summary>
public sealed record UpdateDestination
{
    private readonly Maybe<DestinationType> _type;

    internal UpdateDestination(DestinationId id, ETag etag, Maybe<DestinationType> type)
    {
        Id = id;
        ETag = etag;
        _type = type;
    }

    /// <summary>The destination to change.</summary>
    public DestinationId Id { get; }

    /// <summary>The version the change is based on.</summary>
    public ETag ETag { get; }

    internal Maybe<DestinationName> Name { get; private init; }

    internal Maybe<string> Description { get; private init; }

    internal Maybe<IReadOnlyList<string>> Tags { get; private init; }

    /// <summary>A new name.</summary>
    public UpdateDestination WithName(DestinationName name) => this with { Name = name };

    /// <summary>A new description; empty clears it.</summary>
    public UpdateDestination WithDescription(string description) => this with { Description = description ?? "" };

    /// <summary>New labels, replacing the old ones; the type's tag is kept.</summary>
    public UpdateDestination WithTags(params IReadOnlyList<string> tags) =>
        this with { Tags = Maybe.From(DestinationTypes.WithTypeTag(tags, _type)) };
}

/// <summary>Changes to a destination version, from <see cref="DestinationVersion.Edit"/>.</summary>
public sealed record UpdateDestinationVersion
{
    internal UpdateDestinationVersion(DestinationId destinationId, int version, ETag etag)
    {
        DestinationId = destinationId;
        Version = version;
        ETag = etag;
    }

    /// <summary>The destination the version belongs to.</summary>
    public DestinationId DestinationId { get; }

    /// <summary>The version number to change.</summary>
    public int Version { get; }

    /// <summary>The version of the resource the change is based on.</summary>
    public ETag ETag { get; }

    internal Maybe<IReadOnlyList<DestinationVersionLanguage>> Languages { get; private init; }

    internal Maybe<LanguageCode> DefaultLanguage { get; private init; }

    internal Maybe<IReadOnlyList<string>> AllowedOrigins { get; private init; }

    internal Maybe<string> Description { get; private init; }

    /// <summary>Replaces the languages the API serves.</summary>
    public UpdateDestinationVersion WithLanguages(params IReadOnlyList<DestinationVersionLanguage> languages) =>
        this with { Languages = Maybe.From(languages) };

    /// <summary>A new default language, one of the version's.</summary>
    public UpdateDestinationVersion WithDefaultLanguage(LanguageCode language) => this with { DefaultLanguage = language };

    /// <summary>Replaces the allowed origins; <c>*</c> allows any and stands alone.</summary>
    public UpdateDestinationVersion AllowOrigins(params IReadOnlyList<string> origins) => this with { AllowedOrigins = Maybe.From(origins) };

    /// <summary>A new description; empty clears it.</summary>
    public UpdateDestinationVersion WithDescription(string description) => this with { Description = description ?? "" };
}

/// <summary>A new endpoint of a destination version.</summary>
public sealed record CreateDestinationEndpoint
{
    private CreateDestinationEndpoint(EndpointId id, EndpointName name, EndpointContent content)
    {
        Id = id;
        Name = name;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    internal EndpointId Id { get; }

    internal EndpointName Name { get; }

    internal EndpointContent Content { get; }

    internal Maybe<string> Description { get; private init; }

    /// <summary>
    /// An endpoint; its id — 3 to 50 letters and digits, not <c>mcp</c> or
    /// <c>openapi</c> — becomes part of the published URL.
    /// </summary>
    public static CreateDestinationEndpoint WithId(EndpointId id, EndpointName name, EndpointContent content) => new(id, name, content);

    /// <summary>Free text, up to 200 characters.</summary>
    public CreateDestinationEndpoint Described(string description) => this with { Description = description };
}

/// <summary>
/// Changes to an endpoint, from <see cref="DestinationEndpoint.Edit"/>. On a
/// published version new content waits for the next publish, and the name and
/// description are fixed.
/// </summary>
public sealed record UpdateDestinationEndpoint
{
    internal UpdateDestinationEndpoint(DestinationId destinationId, int version, EndpointId id, ETag etag)
    {
        DestinationId = destinationId;
        Version = version;
        Id = id;
        ETag = etag;
    }

    /// <summary>The destination the endpoint belongs to.</summary>
    public DestinationId DestinationId { get; }

    /// <summary>The version the endpoint belongs to.</summary>
    public int Version { get; }

    /// <summary>The endpoint to change.</summary>
    public EndpointId Id { get; }

    /// <summary>The version of the endpoint the change is based on.</summary>
    public ETag ETag { get; }

    internal Maybe<EndpointName> Name { get; private init; }

    internal Maybe<string> Description { get; private init; }

    internal Maybe<EndpointContent> Content { get; private init; }

    /// <summary>A new name.</summary>
    public UpdateDestinationEndpoint WithName(EndpointName name) => this with { Name = name };

    /// <summary>A new description; empty clears it.</summary>
    public UpdateDestinationEndpoint WithDescription(string description) => this with { Description = description ?? "" };

    /// <summary>Replaces everything the endpoint returns.</summary>
    public UpdateDestinationEndpoint WithContent(EndpointContent content) =>
        this with { Content = content ?? throw new ArgumentNullException(nameof(content)) };
}

/// <summary>
/// Narrows and pages a destination listing, ordered by id. Keep the same
/// filters when following a cursor.
/// </summary>
public sealed record DestinationListQuery
{
    /// <summary>Case-insensitive substring of the name.</summary>
    public Maybe<string> Name { get; init; } = Maybe<string>.None;

    /// <summary>Lower bound on creation time.</summary>
    public Maybe<DateTimeOffset> CreatedFrom { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Upper bound on creation time.</summary>
    public Maybe<DateTimeOffset> CreatedTo { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Lower bound on last modification time.</summary>
    public Maybe<DateTimeOffset> UpdatedFrom { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Upper bound on last modification time.</summary>
    public Maybe<DateTimeOffset> UpdatedTo { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Where to read and how much.</summary>
    public PageRequest Page { get; init; } = new();
}

internal static class DestinationTypes
{
    private static readonly (DestinationType Type, string Tag)[] Tags =
    [
        (DestinationType.Dynamic, "Dynamic"),
        (DestinationType.MultiConsumer, "Multi-consumer"),
        (DestinationType.Static, "Static"),
        (DestinationType.Enterprise, "Enterprise"),
        (DestinationType.PartnerZone, "Partner Zone"),
    ];

    internal static string Tag(DestinationType type) => Tags.First(pair => pair.Type == type).Tag;

    internal static Maybe<DestinationType> Read(IReadOnlyList<string> tags) =>
        Tags.Where(pair => tags.Contains(pair.Tag, StringComparer.Ordinal)).Select(pair => pair.Type).TryFirst();

    internal static IReadOnlyList<string> WithTypeTag(IReadOnlyList<string> tags, Maybe<DestinationType> type) =>
        type.TryGetValue(out var known) && !tags.Contains(Tag(known), StringComparer.Ordinal)
            ? [Tag(known), .. tags]
            : tags;
}
