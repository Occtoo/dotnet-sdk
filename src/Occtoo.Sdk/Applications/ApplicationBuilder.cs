using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.Sources;

namespace Occtoo.Applications;

/// <summary>
/// Assembles an application's settings and grants without knowing how Occtoo
/// spells them — scope keys, <c>source:{id}</c> resource selectors,
/// <c>destination:{id}</c> and <c>api-version:{id}</c> API selectors. Every
/// input is a value object validated at the call site; whether a grant exists
/// is checked by the API against the tenant's access catalog.
/// </summary>
/// <example>
/// <code>
/// var application = CreateApplication.WithName("Catalog reader")
///     .WithScopes(OcctooScopes.ReadSources)
///     .WithSources("products", "assets")
///     .WithDestinations("webshop");
///
/// var update = current.Edit()
///     .WithoutSources("assets")
///     .WithApiVersions("2b1d7f3a-5c2e-4b8f-9a6d-1e0c4f7a8b9c");
/// </code>
/// </example>
public abstract class ApplicationBuilder<TBuilder>
    where TBuilder : ApplicationBuilder<TBuilder>
{
    private const string AllSources = "sources";
    private const string AllDestinations = "destinations";

    private readonly List<string> _tags;
    private readonly List<string> _scopes;
    private readonly List<string> _resources;
    private readonly List<string> _apis;
    private string _name;
    private Maybe<string> _description;

    private protected ApplicationBuilder(ApplicationName name)
    {
        _name = name.Value;
        _tags = [];
        _scopes = [];
        _resources = [];
        _apis = [];
    }

    private protected ApplicationBuilder(Application application)
    {
        _name = application.Name;
        _description = application.Description;
        _tags = [.. application.Tags];
        _scopes = [.. application.ScopeKeys];
        _resources = [.. application.ResourceSelectors];
        _apis = [.. application.ApiSelectors];
    }

    /// <summary>Renames the application.</summary>
    public TBuilder WithName(ApplicationName name)
    {
        _name = name.Value;
        return Self;
    }

    /// <summary>Sets the free-text description.</summary>
    public TBuilder WithDescription(ApplicationDescription description)
    {
        _description = description.Value;
        return Self;
    }

    /// <summary>Clears the description.</summary>
    public TBuilder WithoutDescription()
    {
        _description = Maybe<string>.None;
        return Self;
    }

    /// <summary>Adds labels for grouping and filtering.</summary>
    public TBuilder WithTags(params IReadOnlyList<ApplicationTag> tags) => Add(_tags, [.. tags.Select(tag => tag.Value)]);

    /// <summary>Removes labels.</summary>
    public TBuilder WithoutTags(params IReadOnlyList<ApplicationTag> tags) => Remove(_tags, [.. tags.Select(tag => tag.Value)]);

    /// <summary>Grants capabilities — use the <see cref="Authentication.OcctooScopes"/> constants.</summary>
    public TBuilder WithScopes(params IReadOnlyList<ApplicationScope> scopes) => Add(_scopes, [.. scopes.Select(scope => scope.Value)]);

    /// <summary>Revokes capabilities.</summary>
    public TBuilder WithoutScopes(params IReadOnlyList<ApplicationScope> scopes) => Remove(_scopes, [.. scopes.Select(scope => scope.Value)]);

    /// <summary>Grants access to specific sources.</summary>
    public TBuilder WithSources(params IReadOnlyList<SourceId> sources) => Add(_resources, Selectors(sources));

    /// <summary>Revokes access to specific sources.</summary>
    public TBuilder WithoutSources(params IReadOnlyList<SourceId> sources) => Remove(_resources, Selectors(sources));

    /// <summary>Grants access to every current and future source.</summary>
    public TBuilder WithAllSources() => Add(_resources, [AllSources]);

    /// <summary>Revokes the all-sources grant; specific source grants stay.</summary>
    public TBuilder WithoutAllSources() => Remove(_resources, [AllSources]);

    /// <summary>Grants every current and future API version of specific destinations.</summary>
    public TBuilder WithDestinations(params IReadOnlyList<DestinationId> destinations) => Add(_apis, Selectors(destinations));

    /// <summary>Revokes destination grants.</summary>
    public TBuilder WithoutDestinations(params IReadOnlyList<DestinationId> destinations) => Remove(_apis, Selectors(destinations));

    /// <summary>Grants every protected destination API, current and future.</summary>
    public TBuilder WithAllDestinations() => Add(_apis, [AllDestinations]);

    /// <summary>Revokes the all-destinations grant; specific grants stay.</summary>
    public TBuilder WithoutAllDestinations() => Remove(_apis, [AllDestinations]);

    /// <summary>
    /// Grants specific destination API versions, by id — the version id alone
    /// identifies it, whichever destination it belongs to.
    /// </summary>
    public TBuilder WithApiVersions(params IReadOnlyList<ApiVersionId> versions) => Add(_apis, Selectors(versions));

    /// <summary>Revokes API version grants.</summary>
    public TBuilder WithoutApiVersions(params IReadOnlyList<ApiVersionId> versions) => Remove(_apis, Selectors(versions));

    private protected ApplicationSettings Settings() =>
        new(_name, _description, [.. _tags], [.. _scopes], [.. _resources], [.. _apis]);

    private TBuilder Self => (TBuilder)this;

    private TBuilder Add(List<string> target, IReadOnlyList<string> values)
    {
        foreach (var value in values)
        {
            if (!target.Contains(value, StringComparer.Ordinal))
                target.Add(value);
        }

        return Self;
    }

    private TBuilder Remove(List<string> target, IReadOnlyList<string> values)
    {
        target.RemoveAll(value => values.Contains(value, StringComparer.Ordinal));
        return Self;
    }

    private static IReadOnlyList<string> Selectors(IReadOnlyList<SourceId> sources) =>
        [.. sources.Select(source => $"source:{source.Value}")];

    private static IReadOnlyList<string> Selectors(IReadOnlyList<DestinationId> destinations) =>
        [.. destinations.Select(destination => $"destination:{destination.Value}")];

    // The API spells version ids as 32 hex digits; anything else is passed through for the API to judge.
    private static IReadOnlyList<string> Selectors(IReadOnlyList<ApiVersionId> versions) =>
        [.. versions.Select(version => $"api-version:{(Guid.TryParse(version.Value, out var id) ? id.ToString("N") : version.Value)}")];
}

/// <summary>Builds a <see cref="CreateApplication"/>; converts implicitly.</summary>
public sealed class CreateApplicationBuilder : ApplicationBuilder<CreateApplicationBuilder>
{
    internal CreateApplicationBuilder(ApplicationName name)
        : base(name)
    {
    }

    /// <summary>The settings for <see cref="ApplicationsClient.Create"/>.</summary>
    public CreateApplication Build() => new(Settings());

    /// <summary>Finishes the application implicitly.</summary>
    public static implicit operator CreateApplication(CreateApplicationBuilder builder) => builder.Build();
}

/// <summary>
/// Builds an <see cref="UpdateApplication"/> from an application's current
/// settings and etag; converts implicitly.
/// </summary>
public sealed class UpdateApplicationBuilder : ApplicationBuilder<UpdateApplicationBuilder>
{
    private readonly uint _etag;

    internal UpdateApplicationBuilder(Application application)
        : base(application) => _etag = application.Etag;

    /// <summary>The replacement for <see cref="ApplicationsClient.Update"/>.</summary>
    public UpdateApplication Build() => new(Settings(), _etag);

    /// <summary>Finishes the update implicitly.</summary>
    public static implicit operator UpdateApplication(UpdateApplicationBuilder builder) => builder.Build();
}
