using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Http.Internal;

namespace Occtoo.ManagedTags.Internal;

// Wire mirrors of the managed-tag models. Naming comes from the context's
// Web defaults (camelCase); enums travel as their names. A value's content
// is polymorphic on the wire — a string or a language-keyed object — and
// is sent as { singleValue } or { localizedValue }.

internal sealed record ManagedTagDto
{
    public required Guid Id { get; init; }

    public string DisplayName { get; init; } = "";

    public string? Type { get; init; }

    public Guid? ParentId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastModifiedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public Guid? UpdatedBy { get; init; }

    internal ManagedTag ToModel() => new(
        ManagedTagId.From(Id),
        DisplayName,
        Enums.Read<ManagedTagType>(Type),
        ParentId is { } parent ? Maybe.From(ManagedTagId.From(parent)) : Maybe<ManagedTagId>.None,
        CreatedAt,
        LastModifiedAt is { } modified ? Maybe.From(modified) : Maybe<DateTimeOffset>.None,
        CreatedBy is { } createdBy ? Maybe.From(createdBy) : Maybe<Guid>.None,
        UpdatedBy is { } updatedBy ? Maybe.From(updatedBy) : Maybe<Guid>.None);
}

internal sealed record ManagedTagValueDto
{
    public required string Key { get; init; }

    public JsonElement Value { get; init; }

    public double Order { get; init; }

    public string? ParentKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastModifiedAt { get; init; }

    public Guid CreatedBy { get; init; }

    public Guid? UpdatedBy { get; init; }

    internal ManagedTagValue ToModel() => new(
        ManagedTagValueKey.From(Key),
        ReadContent(Value),
        Order,
        ParentKey is { Length: > 0 } ? Maybe.From(ManagedTagValueKey.From(ParentKey)) : Maybe<ManagedTagValueKey>.None,
        CreatedAt,
        LastModifiedAt is { } modified ? Maybe.From(modified) : Maybe<DateTimeOffset>.None,
        CreatedBy,
        UpdatedBy is { } updatedBy ? Maybe.From(updatedBy) : Maybe<Guid>.None);

    // Content never falls back silently: a shape neither a single string nor a
    // map of strings is a DataTypeError naming the value.
    private ManagedTagValueContent ReadContent(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => ManagedTagValueContent.Text(value.GetString()!),
        JsonValueKind.Object => new ManagedTagValueContent.LocalizedValue(
            value.EnumerateObject().ToDictionary(property => property.Name, Translation, StringComparer.Ordinal)),
        _ => throw new DataTypeException($"Managed tag value '{Key}' is a JSON {value.ValueKind}, not a string or a map of translations."),
    };

    private string Translation(JsonProperty translation) =>
        translation.Value.ValueKind == JsonValueKind.String
            ? translation.Value.GetString()!
            : throw new DataTypeException(
                $"Managed tag value '{Key}' has a '{translation.Name}' translation that is a JSON {translation.Value.ValueKind}, not a string.");
}

internal sealed record ManagedTagValueContentDto(string? SingleValue, IReadOnlyDictionary<string, string>? LocalizedValue)
{
    internal static ManagedTagValueContentDto From(ManagedTagValueContent content) => content switch
    {
        ManagedTagValueContent.TextValue text => new(text.Value, null),
        ManagedTagValueContent.LocalizedValue localized => new(null, localized.Translations),
        _ => throw new InvalidOperationException($"Unknown managed tag value content {content.GetType()}."),
    };
}

internal sealed record CreateManagedTagDto(string DisplayName, ManagedTagType Type, Guid? ParentId);

internal sealed record UpdateManagedTagDto(string DisplayName, Guid? ParentId);

internal sealed record CreateManagedTagValueDto(string Key, ManagedTagValueContentDto Value, double Order, string? ParentKey);

internal sealed record UpdateManagedTagValueDto(ManagedTagValueContentDto Value, double Order, string? ParentKey);

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
[JsonSerializable(typeof(ManagedTagDto))]
[JsonSerializable(typeof(ManagedTagValueDto))]
[JsonSerializable(typeof(CreateManagedTagDto))]
[JsonSerializable(typeof(UpdateManagedTagDto))]
[JsonSerializable(typeof(CreateManagedTagValueDto))]
[JsonSerializable(typeof(UpdateManagedTagValueDto))]
[JsonSerializable(typeof(ForwardPageDto<ManagedTagDto>))]
[JsonSerializable(typeof(ForwardPageDto<ManagedTagValueDto>))]
internal sealed partial class ManagedTagsJsonContext : JsonSerializerContext;
