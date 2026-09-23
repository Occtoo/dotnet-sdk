using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.Sources;

namespace Occtoo.Applications;

/// <summary>
/// Assembles an application's settings and grants without knowing how Occtoo
/// spells them — scope keys, <c>source:{id}</c> resource selectors,
/// <c>destination:{id}</c> and <c>api-version:{id}</c> API selectors. Start
/// with <see cref="CreateApplication.Named"/> for a new application, or
/// <see cref="Application.Edit"/> to change an existing one. Grants are not
/// validated client-side: the API checks them against the tenant's access
/// catalog.
/// </summary>
/// <example>
/// <code>
/// var application = CreateApplication.Named("Catalog reader")
///     .WithScopes(OcctooScopes.ReadSources)
///     .WithSources("products", "assets")
///     .WithDestinations("webshop")
///     .WithApiVersions("2b1d7f3a-5c2e-4b8f-9a6d-1e0c4f7a8b9c");
/// </code>
/// </example>
public sealed class ApplicationBuilder
{
    private readonly List<string> _tags;
    private readonly List<string> _scopes;
    private readonly List<string> _resources;
    private readonly List<string> _apis;
    private string _name;
    private Maybe<string> _description;

    internal ApplicationBuilder(string name)
        : this(name, Maybe<string>.None, [], [], [], [])
    {
    }

    internal ApplicationBuilder(Application application)
        : this(application.Name, application.Description, application.Tags, application.ScopeKeys,
            application.ResourceSelectors, application.ApiSelectors)
    {
    }

    private ApplicationBuilder(
        string name,
        Maybe<string> description,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> resources,
        IReadOnlyList<string> apis)
    {
        _name = name;
        _description = description;
        _tags = [.. tags];
        _scopes = [.. scopes];
        _resources = [.. resources];
        _apis = [.. apis];
    }

    /// <summary>Renames the application.</summary>
    public ApplicationBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the free-text description.</summary>
    public ApplicationBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    /// <summary>Adds labels for grouping and filtering.</summary>
    public ApplicationBuilder WithTags(params IReadOnlyList<string> tags) => Add(_tags, tags);

    /// <summary>Grants capabilities — use the <see cref="Authentication.OcctooScopes"/> constants.</summary>
    public ApplicationBuilder WithScopes(params IReadOnlyList<string> scopes) => Add(_scopes, scopes);

    /// <summary>Grants access to specific sources.</summary>
    public ApplicationBuilder WithSources(params IReadOnlyList<SourceId> sources) =>
        Add(_resources, [.. sources.Select(source => $"source:{source.Value}")]);

    /// <summary>Grants access to every current and future source.</summary>
    public ApplicationBuilder WithAllSources() => Add(_resources, ["sources"]);

    /// <summary>Grants every current and future API version of specific destinations.</summary>
    public ApplicationBuilder WithDestinations(params IReadOnlyList<DestinationId> destinations) =>
        Add(_apis, [.. destinations.Select(destination => $"destination:{destination.Value}")]);

    /// <summary>Grants every protected destination API, current and future.</summary>
    public ApplicationBuilder WithAllDestinations() => Add(_apis, ["destinations"]);

    /// <summary>
    /// Grants specific destination API versions, by id — the version id alone
    /// identifies it, whichever destination it belongs to.
    /// </summary>
    public ApplicationBuilder WithApiVersions(params IReadOnlyList<ApiVersionId> versions) =>
        Add(_apis, [.. versions.Select(version => $"api-version:{ApiVersionKey(version)}")]);

    /// <summary>The settings for <see cref="ApplicationsClient.Create"/>.</summary>
    public CreateApplication Build() => new(_name)
    {
        Description = _description,
        Tags = [.. _tags],
        ScopeKeys = [.. _scopes],
        ResourceSelectors = [.. _resources],
        ApiSelectors = [.. _apis],
    };

    /// <summary>
    /// The full replacement for <see cref="ApplicationsClient.Update"/>, guarded
    /// by the etag of the read it is based on.
    /// </summary>
    public UpdateApplication BuildUpdate(uint etag) => new(_name, etag)
    {
        Description = _description,
        Tags = [.. _tags],
        ScopeKeys = [.. _scopes],
        ResourceSelectors = [.. _resources],
        ApiSelectors = [.. _apis],
    };

    /// <summary>Finishes a new application implicitly.</summary>
    public static implicit operator CreateApplication(ApplicationBuilder builder) => builder.Build();

    private ApplicationBuilder Add(List<string> target, IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            if (!target.Contains(value, StringComparer.Ordinal))
                target.Add(value);
        }

        return this;
    }

    // The API spells version ids as 32 hex digits; anything else is passed through for the API to judge.
    private static string ApiVersionKey(ApiVersionId version) =>
        Guid.TryParse(version.Value, out var id) ? id.ToString("N") : version.Value;
}
