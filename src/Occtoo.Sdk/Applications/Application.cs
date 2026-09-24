using CSharpFunctionalExtensions;
using Occtoo.Authentication;
using Vogen;

namespace Occtoo.Applications;

/// <summary>The id of a tenant application.</summary>
[ValueObject<Guid>]
public readonly partial struct TenantApplicationId
{
    private static Validation Validate(Guid input) =>
        input == Guid.Empty ? Validation.Invalid("An application id must not be empty.") : Validation.Ok;
}

/// <summary>
/// A machine-to-machine application registered in the tenant. <c>Etag</c>
/// is the concurrency token <see cref="ApplicationsClient.Update"/> requires;
/// read responses never include the client secret.
/// </summary>
public sealed record Application(
    TenantApplicationId Id,
    string Name,
    Maybe<string> Description,
    ClientId ClientId,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ScopeKeys,
    IReadOnlyList<string> ResourceSelectors,
    IReadOnlyList<string> ApiSelectors,
    IReadOnlyList<string> Audiences,
    uint Etag,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt)
{
    /// <summary>
    /// Starts changing this application: a builder pre-filled with its current
    /// settings and grants, carrying its etag into the update.
    /// </summary>
    public UpdateApplicationBuilder Edit() => new(this);
}

/// <summary>
/// A freshly created application and its client secret. The secret is
/// returned exactly once — store it; no later read can recover it.
/// </summary>
public sealed record ApplicationCredentials(Application Application, ClientSecret ClientSecret);

/// <summary>
/// One option in the access catalog — a scope, a resource, or a destination
/// API selection an application may be granted — with the selections nested
/// under it.
/// </summary>
public sealed record AccessNode(
    string Key,
    string GrantType,
    string Label,
    Maybe<string> Description,
    Maybe<string> ResourceId,
    Maybe<string> Audience,
    IReadOnlyList<AccessNode> Children);

/// <summary>
/// A new application, assembled by <see cref="WithName"/>'s builder — the
/// only way to create one, so every input has been validated.
/// </summary>
public sealed record CreateApplication
{
    internal CreateApplication(ApplicationSettings settings) => Settings = settings;

    /// <summary>Starts building a new application.</summary>
    public static CreateApplicationBuilder WithName(ApplicationName name) => new(name);

    internal ApplicationSettings Settings { get; }

    /// <summary>The application's name.</summary>
    public string Name => Settings.Name;

    /// <summary>A free-text description.</summary>
    public Maybe<string> Description => Settings.Description;

    /// <summary>Labels for grouping and filtering.</summary>
    public IReadOnlyList<string> Tags => Settings.Tags;

    /// <summary>The scope keys to grant.</summary>
    public IReadOnlyList<string> ScopeKeys => Settings.ScopeKeys;

    /// <summary>The resource selectors to grant.</summary>
    public IReadOnlyList<string> ResourceSelectors => Settings.ResourceSelectors;

    /// <summary>The destination API selectors to grant.</summary>
    public IReadOnlyList<string> ApiSelectors => Settings.ApiSelectors;
}

/// <summary>
/// The full replacement of an application's settings, assembled by
/// <see cref="Application.Edit"/>. Carries the etag of the read it started
/// from; a concurrent change in between is rejected with a
/// <see cref="ConflictError"/>.
/// </summary>
public sealed record UpdateApplication
{
    internal UpdateApplication(ApplicationSettings settings, uint etag)
    {
        Settings = settings;
        Etag = etag;
    }

    internal ApplicationSettings Settings { get; }

    /// <summary>The etag of the read this update is based on.</summary>
    public uint Etag { get; }

    /// <inheritdoc cref="CreateApplication.Name"/>
    public string Name => Settings.Name;

    /// <inheritdoc cref="CreateApplication.Description"/>
    public Maybe<string> Description => Settings.Description;

    /// <inheritdoc cref="CreateApplication.Tags"/>
    public IReadOnlyList<string> Tags => Settings.Tags;

    /// <inheritdoc cref="CreateApplication.ScopeKeys"/>
    public IReadOnlyList<string> ScopeKeys => Settings.ScopeKeys;

    /// <inheritdoc cref="CreateApplication.ResourceSelectors"/>
    public IReadOnlyList<string> ResourceSelectors => Settings.ResourceSelectors;

    /// <inheritdoc cref="CreateApplication.ApiSelectors"/>
    public IReadOnlyList<string> ApiSelectors => Settings.ApiSelectors;
}

internal sealed record ApplicationSettings(
    string Name,
    Maybe<string> Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ScopeKeys,
    IReadOnlyList<string> ResourceSelectors,
    IReadOnlyList<string> ApiSelectors);
/// <summary>
/// Narrows and pages an application listing. Filters combine with AND;
/// timestamp bounds are inclusive. Keep the same filters when following
/// <see cref="Page"/>'s cursor.
/// </summary>
public sealed record ApplicationListQuery
{
    /// <summary>Case-insensitive substring of the name.</summary>
    public Maybe<string> Name { get; init; } = Maybe<string>.None;

    /// <summary>Exact tags; every one must be present.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Lower bound on creation time.</summary>
    public Maybe<DateTimeOffset> CreatedFrom { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Upper bound on creation time.</summary>
    public Maybe<DateTimeOffset> CreatedTo { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Lower bound on last modification time.</summary>
    public Maybe<DateTimeOffset> UpdatedFrom { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>Upper bound on last modification time.</summary>
    public Maybe<DateTimeOffset> UpdatedTo { get; init; } = Maybe<DateTimeOffset>.None;

    /// <summary>The actor that created the application.</summary>
    public Maybe<Guid> CreatedBy { get; init; } = Maybe<Guid>.None;

    /// <summary>The actor that last modified the application.</summary>
    public Maybe<Guid> UpdatedBy { get; init; } = Maybe<Guid>.None;

    /// <summary>Where to read and how much.</summary>
    public PageRequest Page { get; init; } = new();
}
