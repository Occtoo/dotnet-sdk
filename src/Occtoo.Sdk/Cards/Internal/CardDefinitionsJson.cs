using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.Http.Internal;
using Occtoo.ManagedTags;
using Occtoo.Sources;

namespace Occtoo.Cards.Internal;

// Wire mirrors of card definitions and card exports. Naming comes from the
// context's Web defaults (camelCase); enums read leniently. A where travels
// as the raw JSON Where renders.

internal sealed record ManagedTagListDto(Guid ListId, string? ValueType)
{
    internal Maybe<ManagedTagList> ToModel() =>
        Enums.Read<ManagedTagValuePart>(ValueType).Map(part => new ManagedTagList(ManagedTagId.From(ListId), part));

    internal static ManagedTagListDto From(ManagedTagList list) => new(list.ListId.Value, list.Stores.ToString());
}

internal sealed record CardPropertyDto
{
    public required string Id { get; init; }

    public required string SourceId { get; init; }

    public required string SourcePropertyId { get; init; }

    public string? Type { get; init; }

    public Dictionary<string, string>? Name { get; init; }

    public Dictionary<string, string>? Description { get; init; }

    public Dictionary<string, string>? Settings { get; init; }

    public string? Delimiter { get; init; }

    public decimal Order { get; init; }

    public bool IsHidden { get; init; }

    public bool IsEditable { get; init; }

    public ManagedTagListDto? ManagedTagList { get; init; }

    internal CardProperty ToModel() => new(
        CardPropertyId.Read(Id),
        Sources.SourceId.From(SourceId),
        PropertyId.From(SourcePropertyId),
        Enums.Read<SourcePropertyType>(Type),
        Translations(Name),
        Translations(Description),
        Settings ?? [],
        Sources.Delimiter.Read(Delimiter),
        Order,
        IsHidden,
        IsEditable,
        ManagedTagList?.ToModel() ?? Maybe<ManagedTagList>.None);

    internal static Result<CardPropertyDto, OcctooError> From(CardProperty property) =>
        property.Type.TryGetValue(out var type)
            ? new CardPropertyDto
            {
                Id = property.Id.Value,
                SourceId = property.SourceId.Value,
                SourcePropertyId = property.SourcePropertyId.Value,
                Type = type.ToString(),
                Name = property.Name.ToDictionary(pair => pair.Key.Value, pair => pair.Value),
                Description = property.Description.ToDictionary(pair => pair.Key.Value, pair => pair.Value),
                Settings = property.Settings.ToDictionary(),
                Delimiter = property.Delimiter.Map(delimiter => delimiter.Value).GetValueOrDefault(),
                Order = property.Order,
                IsHidden = property.IsHidden,
                IsEditable = property.IsEditable,
                ManagedTagList = property.ManagedTagList.Map(ManagedTagListDto.From).GetValueOrDefault(),
            }
            : new ValidationError($"Card property '{property.Id}' has no type.");

    internal static Result<List<CardPropertyDto>, OcctooError> From(IReadOnlyList<CardProperty> properties)
    {
        var items = new List<CardPropertyDto>(properties.Count);
        foreach (var property in properties)
        {
            var item = From(property);
            if (item.IsFailure)
                return item.Error;
            items.Add(item.Value);
        }

        return items;
    }

    private static Dictionary<LanguageCode, string> Translations(Dictionary<string, string>? translations) =>
        (translations ?? [])
            .Select(pair => (Code: LanguageCode.Read(pair.Key), pair.Value))
            .Where(pair => pair.Code.HasValue)
            .ToDictionary(pair => pair.Code.Value, pair => pair.Value);
}

internal sealed record PropertyGroupDto(string Name, IReadOnlyList<string> PropertyIds)
{
    internal PropertyGroup ToModel() => new(Name, [.. PropertyIds.Select(CardPropertyId.Read)]);

    internal static PropertyGroupDto From(PropertyGroup group) => new(group.Name, [.. group.PropertyIds.Select(id => id.Value)]);
}

internal sealed record CardPropertyChangesDto
{
    public List<CardPropertyDto>? Set { get; init; }

    public List<string>? Remove { get; init; }

    internal CardPropertyChanges ToModel() =>
        new([.. (Set ?? []).Select(property => property.ToModel())], [.. (Remove ?? []).Select(CardPropertyId.Read)]);
}

internal static class CardDisplayDto
{
    internal static CardDisplay ToModel(Dictionary<string, string>? options) => new(
        Role(options, "name"),
        Role(options, "description"),
        Role(options, "image"));

    internal static Dictionary<string, string> From(CardDisplay display)
    {
        var options = new Dictionary<string, string>();
        if (display.Name.TryGetValue(out var name))
            options["name"] = name.Value;
        if (display.Description.TryGetValue(out var description))
            options["description"] = description.Value;
        if (display.Image.TryGetValue(out var image))
            options["image"] = image.Value;
        return options;
    }

    private static Maybe<CardPropertyId> Role(Dictionary<string, string>? options, string role) =>
        options is not null && options.TryGetValue(role, out var id) && id is { Length: > 0 }
            ? CardPropertyId.Read(id)
            : Maybe<CardPropertyId>.None;
}

internal sealed record CardDefinitionSummaryDto
{
    public required string Id { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public string? Status { get; init; }

    public uint Etag { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    internal CardDefinitionSummary ToModel() => new(
        CardDefinitionId.From(Id),
        Name,
        Description is { Length: > 0 } description ? description : Maybe<string>.None,
        Tags ?? [],
        Enums.Read<CardDefinitionStatus>(Status),
        ETag.From(Etag),
        CreatedAt,
        UpdatedAt);
}

internal sealed record CardDefinitionDto
{
    public required string Id { get; init; }

    public string Name { get; init; } = "";

    public string? Description { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public string? Status { get; init; }

    public uint Etag { get; init; }

    public Dictionary<string, string>? DisplayOptions { get; init; }

    public IReadOnlyList<PropertyGroupDto>? PropertyGroups { get; init; }

    public IReadOnlyList<CardPropertyDto>? Properties { get; init; }

    public CardPropertyChangesDto? PendingChanges { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    internal CardDefinition ToModel() => new(
        CardDefinitionId.From(Id),
        Name,
        Description is { Length: > 0 } description ? description : Maybe<string>.None,
        Tags ?? [],
        Enums.Read<CardDefinitionStatus>(Status),
        ETag.From(Etag),
        CardDisplayDto.ToModel(DisplayOptions),
        [.. (PropertyGroups ?? []).Select(group => group.ToModel())],
        [.. (Properties ?? []).Select(property => property.ToModel())],
        PendingChanges?.ToModel() ?? Maybe<CardPropertyChanges>.None,
        CreatedAt,
        UpdatedAt);
}

internal sealed record CreateCardDefinitionDto(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<string>? Tags,
    Dictionary<string, string>? DisplayOptions,
    IReadOnlyList<PropertyGroupDto>? PropertyGroups,
    IReadOnlyList<CardPropertyDto>? Properties);

internal sealed record CardPropertyChangesRequestDto(IReadOnlyList<CardPropertyDto> Set, IReadOnlyList<string> Remove);

internal sealed record UpdateCardDefinitionDto(
    string? Name,
    string? Description,
    IReadOnlyList<string>? Tags,
    Dictionary<string, string>? DisplayOptions,
    IReadOnlyList<PropertyGroupDto>? PropertyGroups,
    CardPropertyChangesRequestDto? PendingChanges);

internal sealed record CardDefinitionPageDto
{
    public required CardDefinitionSummaryDto[] Items { get; init; }

    public string? After { get; init; }

    public long? TotalCount { get; init; }
}

internal sealed record CardExportFileDto(string Name, long? SizeBytes, string Url);

internal sealed record CardExportDto
{
    public required Guid Id { get; init; }

    public string? Status { get; init; }

    public double? Progress { get; init; }

    public int? TotalEntries { get; init; }

    public int? ProcessedEntries { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public CardExportFileDto? File { get; init; }

    internal CardExport ToModel() => new(
        CardExportId.From(Id),
        Enums.Read<CardExportStatus>(Status),
        Progress is { } progress ? progress : Maybe<double>.None,
        TotalEntries is { } total ? total : Maybe<int>.None,
        ProcessedEntries is { } processed ? processed : Maybe<int>.None,
        CreatedAt,
        UpdatedAt,
        ExpiresAt,
        File is { } file
            ? new CardExportFile(file.Name, file.SizeBytes is { } size ? size : Maybe<long>.None, new Uri(file.Url, UriKind.Absolute))
            : Maybe<CardExportFile>.None);
}

internal sealed record StartCardExportDto(
    string Format,
    JsonElement? Where,
    string? Language,
    IReadOnlyList<string>? Properties,
    IReadOnlyList<string>? Languages,
    bool IncludeTimestamp,
    string? Delimiter,
    string? Filename)
{
    internal static StartCardExportDto From(StartCardExport export) => new(
        export.Format == CardExportFormat.Csv ? "CSV" : "Excel",
        export.Where.Map(WhereJson.Element).GetValueOrDefault(),
        export.WhereLanguage.Map(language => language.Value).GetValueOrDefault(),
        export.Properties.Map(IReadOnlyList<string> (ids) => [.. ids.Select(id => id.Value)]).GetValueOrDefault(),
        export.Languages.Map(IReadOnlyList<string> (codes) => [.. codes.Select(code => code.Value)]).GetValueOrDefault(),
        export.IncludeTimestamp,
        export.Delimiter.Map(delimiter => delimiter.Value).GetValueOrDefault(),
        export.Filename.GetValueOrDefault());
}

internal static class WhereJson
{
    internal static JsonElement? Element(Where where)
    {
        using var document = JsonDocument.Parse(where.ToString());
        return document.RootElement.Clone();
    }
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CardDefinitionDto))]
[JsonSerializable(typeof(CardDefinitionPageDto))]
[JsonSerializable(typeof(CreateCardDefinitionDto))]
[JsonSerializable(typeof(UpdateCardDefinitionDto))]
[JsonSerializable(typeof(CardExportDto))]
[JsonSerializable(typeof(StartCardExportDto))]
internal sealed partial class CardDefinitionsJsonContext : JsonSerializerContext;
