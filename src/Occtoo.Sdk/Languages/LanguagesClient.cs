using CSharpFunctionalExtensions;
using Occtoo.Http.Internal;
using Occtoo.Languages.Internal;

namespace Occtoo.Languages;

/// <summary>
/// The tenant's languages. Reached through <see cref="OcctooClient.Languages"/>.
/// Reads require <see cref="Authentication.OcctooScopes.ReadCards"/>, writes
/// <see cref="Authentication.OcctooScopes.WriteCards"/>.
/// </summary>
public sealed class LanguagesClient
{
    private static readonly Uri Route = new("v1/languages", UriKind.Relative);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    internal LanguagesClient(HttpClient httpClient, TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _requestTimeout = requestTimeout;
    }

    /// <summary>Reads the tenant's languages, ordered by code, and its default language.</summary>
    public Task<Result<TenantLanguages, OcctooError>> Get(CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(_httpClient, _requestTimeout, OcctooTransport.Request(HttpMethod.Get, Route), "get languages",
                LanguagesJsonContext.Default.LanguagesDto, cancellationToken)
            .MapResponse(dto => dto.ToModel());

    /// <summary>
    /// Replaces the tenant's languages and default language. A language left
    /// out is removed — its data stays, and it comes back only when a source
    /// first receives entries in it — so start from <see cref="Get"/>.
    /// </summary>
    public Task<Result<TenantLanguages, OcctooError>> Replace(
        TenantLanguages languages,
        CancellationToken cancellationToken = default)
    {
        if (languages is null)
            return Task.FromResult(Result.Failure<TenantLanguages, OcctooError>(new ValidationError("Languages are required.")));

        if (languages.Default.TryGetValue(out var code) && languages.Items.All(language => language.Code != code))
            return Task.FromResult(Result.Failure<TenantLanguages, OcctooError>(
                new ValidationError($"The default language '{code}' is not one of the languages.")));

        return OcctooTransport.Send(_httpClient, _requestTimeout,
                OcctooTransport.Request(HttpMethod.Put, Route, LanguagesDto.From(languages), LanguagesJsonContext.Default.LanguagesDto),
                "replace languages", LanguagesJsonContext.Default.LanguagesDto, cancellationToken)
            .MapResponse(dto => dto.ToModel());
    }
}
