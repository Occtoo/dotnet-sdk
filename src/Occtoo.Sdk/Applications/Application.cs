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
    DateTimeOffset LastModifiedAt);

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
/// A new application. Use <see cref="ApplicationsClient.GetAccessCatalog"/>
/// to discover valid scope keys, resource selectors, and API selectors.
/// </summary>
public sealed record CreateApplication(string Name)
{
    /// <summary>A free-text description.</summary>
    public Maybe<string> Description { get; init; } = Maybe<string>.None;

    /// <summary>Labels for grouping and filtering.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The scopes to grant — <c>read:sources</c>, <c>write:sources</c>, …</summary>
    public IReadOnlyList<string> ScopeKeys { get; init; } = [];

    /// <summary>The resources to grant — <c>source:products</c>, or <c>sources</c> for all.</summary>
    public IReadOnlyList<string> ResourceSelectors { get; init; } = [];

    /// <summary>The destination APIs to grant.</summary>
    public IReadOnlyList<string> ApiSelectors { get; init; } = [];
}

/// <summary>
/// Replaces every setting of an application. <c>Etag</c> must be the one
/// from the latest read — a stale value is rejected with a
/// <see cref="ConflictError"/>. Collections left empty become empty.
/// </summary>
public sealed record UpdateApplication(string Name, uint Etag)
{
    /// <inheritdoc cref="CreateApplication.Description"/>
    public Maybe<string> Description { get; init; } = Maybe<string>.None;

    /// <inheritdoc cref="CreateApplication.Tags"/>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <inheritdoc cref="CreateApplication.ScopeKeys"/>
    public IReadOnlyList<string> ScopeKeys { get; init; } = [];

    /// <inheritdoc cref="CreateApplication.ResourceSelectors"/>
    public IReadOnlyList<string> ResourceSelectors { get; init; } = [];

    /// <inheritdoc cref="CreateApplication.ApiSelectors"/>
    public IReadOnlyList<string> ApiSelectors { get; init; } = [];
}

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
