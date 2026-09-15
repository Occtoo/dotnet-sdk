using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Authentication;

namespace Occtoo.Applications.Internal;

// Wire mirrors of the public models. Naming comes from the context's Web
// defaults (camelCase); enums travel as their names.

internal sealed record ApplicationDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public string ClientId { get; init; } = "";

    public string[] Tags { get; init; } = [];

    public string[] ScopeKeys { get; init; } = [];

    public string[] ResourceSelectors { get; init; } = [];

    public string[] ApiSelectors { get; init; } = [];

    public string[] Audiences { get; init; } = [];

    public uint Etag { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastModifiedAt { get; init; }

    internal Application ToModel() => new(
        TenantApplicationId.From(Id),
        Name,
        Description is { Length: > 0 } ? Maybe.From(Description) : Maybe<string>.None,
        Authentication.ClientId.From(ClientId),
        Tags,
        ScopeKeys,
        ResourceSelectors,
        ApiSelectors,
        Audiences,
        Etag,
        CreatedAt,
        LastModifiedAt);
}

internal sealed record ApplicationCredentialsDto
{
    public ApplicationDto Application { get; init; } = new();

    public string ClientSecret { get; init; } = "";

    internal ApplicationCredentials ToModel() =>
        new(Application.ToModel(), Authentication.ClientSecret.From(ClientSecret));
}

internal sealed record AccessNodeDto
{
    public string Key { get; init; } = "";

    public string GrantType { get; init; } = "";

    public string Label { get; init; } = "";

    public string? Description { get; init; }

    public string? ResourceId { get; init; }

    public string? Audience { get; init; }

    public AccessNodeDto[] Children { get; init; } = [];

    internal AccessNode ToModel() => new(
        Key,
        GrantType,
        Label,
        Description is { Length: > 0 } ? Maybe.From(Description) : Maybe<string>.None,
        ResourceId is { Length: > 0 } ? Maybe.From(ResourceId) : Maybe<string>.None,
        Audience is { Length: > 0 } ? Maybe.From(Audience) : Maybe<string>.None,
        [.. Children.Select(child => child.ToModel())]);
}

internal sealed record CreateApplicationDto(
    string Name,
    string? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ScopeKeys,
    IReadOnlyList<string> ResourceSelectors,
    IReadOnlyList<string> ApiSelectors);

internal sealed record UpdateApplicationDto(
    string Name,
    uint Etag,
    string? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ScopeKeys,
    IReadOnlyList<string> ResourceSelectors,
    IReadOnlyList<string> ApiSelectors);

internal sealed record ForwardPageDto<T>
{
    public T[] Items { get; init; } = [];

    public string? After { get; init; }

    public long? TotalCount { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ApplicationDto))]
[JsonSerializable(typeof(ApplicationCredentialsDto))]
[JsonSerializable(typeof(AccessNodeDto[]))]
[JsonSerializable(typeof(CreateApplicationDto))]
[JsonSerializable(typeof(UpdateApplicationDto))]
[JsonSerializable(typeof(ForwardPageDto<ApplicationDto>))]
internal sealed partial class ApplicationsJsonContext : JsonSerializerContext;
