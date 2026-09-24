using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Http.Internal;

namespace Occtoo.Sources.Internal;

// Wire mirrors of the source-management models. Naming comes from the
// context's Web defaults (camelCase); enums travel as their names.

internal sealed record SourceDto
{
    public required string Id { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public string? Status { get; init; }

    public string? Type { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    internal Source ToModel() => new(
        SourceId.From(Id),
        Name,
        Description is { Length: > 0 } ? Maybe.From(Description) : Maybe<string>.None,
        Enums.Read<SourceStatus>(Status),
        Enums.Read<SourceType>(Type),
        CreatedAt,
        UpdatedAt);
}

internal sealed record SourcePropertyDto
{
    public required string Id { get; init; }

    public string DisplayName { get; init; } = "";

    public string? Description { get; init; }

    public string? Type { get; init; }

    public string? Delimiter { get; init; }

    public string? State { get; init; }

    internal SourceProperty ToModel() => new(
        PropertyId.From(Id),
        DisplayName,
        Description is { Length: > 0 } ? Maybe.From(Description) : Maybe<string>.None,
        Enums.Read<SourcePropertyType>(Type),
        Sources.Delimiter.Read(Delimiter),
        Enums.Read<SourcePropertyState>(State));
}

internal sealed record CreateSourceDto(string Id, string Name, string? Description);

internal sealed record UpdateSourceDto(string? Name, string? Description);

internal sealed record UpsertSourcePropertyDto(
    string DisplayName,
    SourcePropertyType? Type,
    string? Delimiter,
    string? Description);

internal sealed record ForwardPageDto<T>
{
    public required T[] Items { get; init; }

    public string? After { get; init; }

    public long? TotalCount { get; init; }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SourceDto))]
[JsonSerializable(typeof(SourcePropertyDto))]
[JsonSerializable(typeof(CreateSourceDto))]
[JsonSerializable(typeof(UpdateSourceDto))]
[JsonSerializable(typeof(UpsertSourcePropertyDto))]
[JsonSerializable(typeof(ForwardPageDto<SourceDto>))]
[JsonSerializable(typeof(ForwardPageDto<SourcePropertyDto>))]
internal sealed partial class SourcesJsonContext : JsonSerializerContext;

