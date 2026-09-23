using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Occtoo.Applications.Internal;
using Occtoo.Http.Internal;
using Occtoo.Logging;

namespace Occtoo.Applications;

/// <summary>
/// The Applications feature — managing the tenant's machine-to-machine
/// applications and their grants. Reached through
/// <see cref="OcctooClient.Applications"/>.
/// </summary>
/// <remarks>
/// Reads require <see cref="Authentication.OcctooScopes.ReadApplications"/>;
/// writes require <see cref="Authentication.OcctooScopes.WriteApplications"/>,
/// an administrative capability: it creates identities and changes their
/// grants.
/// </remarks>
public sealed class ApplicationsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly TimeSpan _requestTimeout;

    internal ApplicationsClient(HttpClient httpClient, ILogger logger, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _logger = logger;
        _requestTimeout = requestTimeout;
    }

    /// <summary>Reads one page of applications.</summary>
    public Task<Result<Page<Application>, OcctooError>> List(
        ApplicationListQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new ApplicationListQuery();
        if (Pages.Validate(query.Page) is { HasValue: true } invalid)
            return Task.FromResult(Result.Failure<Page<Application>, OcctooError>(invalid.Value));

        var uri = new QueryString("v1/applications")
            .Add("name", query.Name)
            .AddEach("tags", query.Tags)
            .Add("createdFrom", query.CreatedFrom)
            .Add("createdTo", query.CreatedTo)
            .Add("updatedFrom", query.UpdatedFrom)
            .Add("updatedTo", query.UpdatedTo)
            .Add("createdBy", query.CreatedBy)
            .Add("updatedBy", query.UpdatedBy)
            .Add(query.Page)
            .ToUri();

        return OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, uri), "list applications",
                ApplicationsJsonContext.Default.ForwardPageDtoApplicationDto, cancellationToken)
            .MapResponse(page => Pages.ToPage(page.Items, page.After, page.TotalCount, dto => dto.ToModel()));
    }


    /// <summary>Reads one application.</summary>
    public Task<Result<Application, OcctooError>> Get(
        TenantApplicationId applicationId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Get, Uri(applicationId)),
                "get application", ApplicationsJsonContext.Default.ApplicationDto, cancellationToken)
            .MapResponse(dto => dto.ToModel());

    /// <summary>
    /// Creates an application. The returned client secret is shown exactly
    /// once.
    /// </summary>
    public Task<Result<ApplicationCredentials, OcctooError>> Create(
        CreateApplication application,
        CancellationToken cancellationToken = default)
    {
        if (application is null)
            return Task.FromResult(Result.Failure<ApplicationCredentials, OcctooError>(new ValidationError("An application is required.")));

        var body = new CreateApplicationDto(
            application.Name,
            application.Description.GetValueOrDefault(),
            application.Tags,
            application.ScopeKeys,
            application.ResourceSelectors,
            application.ApiSelectors);

        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Post, new Uri("v1/applications", UriKind.Relative), body,
                    ApplicationsJsonContext.Default.CreateApplicationDto),
                "create application", ApplicationsJsonContext.Default.ApplicationCredentialsDto, cancellationToken)
            .MapResponse(dto => dto.ToModel())
            .Tap(created => OcctooLog.ApplicationCreated(_logger, created.Application.Id.Value, created.Application.Name));
    }

    /// <summary>
    /// Replaces an application's settings and grants. A stale
    /// <see cref="UpdateApplication.Etag"/> fails with <see cref="ConflictError"/>.
    /// </summary>
    public Task<Result<Application, OcctooError>> Update(
        TenantApplicationId applicationId,
        UpdateApplication application,
        CancellationToken cancellationToken = default)
    {
        if (application is null)
            return Task.FromResult(Result.Failure<Application, OcctooError>(new ValidationError("An application is required.")));

        var body = new UpdateApplicationDto(
            application.Name,
            application.Etag,
            application.Description.GetValueOrDefault(),
            application.Tags,
            application.ScopeKeys,
            application.ResourceSelectors,
            application.ApiSelectors);

        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Put, Uri(applicationId), body, ApplicationsJsonContext.Default.UpdateApplicationDto),
                "update application", ApplicationsJsonContext.Default.ApplicationDto, cancellationToken)
            .MapResponse(dto => dto.ToModel());
    }

    /// <summary>
    /// Deletes an application and revokes its credentials. Deleting an
    /// application that no longer exists succeeds.
    /// </summary>
    public Task<UnitResult<OcctooError>> Delete(
        TenantApplicationId applicationId,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Delete, Uri(applicationId)), "delete application", cancellationToken)
            .Tap(() => OcctooLog.ApplicationDeleted(_logger, applicationId.Value));

    /// <summary>
    /// The scopes, resources, and destination APIs an application in this
    /// tenant can be granted — the vocabulary for
    /// <see cref="CreateApplication"/> and <see cref="UpdateApplication"/>.
    /// </summary>
    public Task<Result<IReadOnlyList<AccessNode>, OcctooError>> GetAccessCatalog(
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Get, new Uri("v1/applications/access-catalog", UriKind.Relative)),
                "get application access catalog", ApplicationsJsonContext.Default.AccessNodeDtoArray, cancellationToken)
            .MapResponse(IReadOnlyList<AccessNode> (nodes) => [.. nodes.Select(node => node.ToModel())]);

    private static Uri Uri(TenantApplicationId applicationId) =>
        new($"v1/applications/{applicationId.Value:D}", UriKind.Relative);
}
