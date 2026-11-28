using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Occtoo.Events;
using Occtoo.Http.Internal;
using Occtoo.Sources;
using Occtoo.Sources.Internal;

namespace Occtoo.Cards.Internal;

internal sealed record SortDto(string Property, string Direction, string? Mode);

internal sealed record FacetSortDto(string By, string Direction);

internal sealed record FacetQueryDto(
    string Property,
    string? Contains,
    bool IgnoreCase,
    FacetSortDto? Sort,
    int First,
    string? After,
    IReadOnlyList<string>? Exclude);

internal sealed record SearchCardEntriesDto(
    JsonElement? Where,
    string? Language,
    IReadOnlyList<SortDto> Sort,
    IReadOnlyList<string> Properties,
    int Limit,
    string? After,
    bool IncludeEntries,
    bool IncludeTotalCount,
    bool IncludeSegments,
    IReadOnlyList<FacetQueryDto> Facets)
{
    internal static SearchCardEntriesDto From(CardSearch search) => new(
        search.Where.Map(WhereJson.Element).GetValueOrDefault(),
        search.Language.Map(language => language.Value).GetValueOrDefault(),
        [.. search.Sort.Select(sort => new SortDto(sort.Property.Value, sort.Direction.ToString(), sort.Mode.Map(mode => mode.ToString()).GetValueOrDefault()))],
        [.. search.Properties.Select(property => property.Value)],
        search.Limit,
        search.After.Map(cursor => cursor.Value).GetValueOrDefault(),
        search.IncludeEntries,
        search.IncludeTotal,
        search.IncludeSegments,
        [.. search.Facets.Select(facet => new FacetQueryDto(
            facet.Property.Value,
            facet.Contains.GetValueOrDefault(),
            facet.IgnoreCase,
            facet.Sort.Map(sort => new FacetSortDto(sort.By.ToString(), sort.Direction.ToString())).GetValueOrDefault(),
            facet.First,
            facet.After.Map(cursor => cursor.Value).GetValueOrDefault(),
            facet.Exclude.Count > 0 ? facet.Exclude : null))]);
}

internal sealed record CardEntryPropertyDto
{
    public required string Id { get; init; }

    public JsonElement Value { get; init; }

    public string? Type { get; init; }

    public string? Delimiter { get; init; }

    public DateTimeOffset? LastUpdated { get; init; }

    public string? Language { get; init; }

    internal CardEntryValue ToModel()
    {
        var type = Enums.Read<SourcePropertyType>(Type);
        return new(
            CardPropertyId.Read(Id),
            StoredValues.Read(Value, type, Id),
            type,
            Sources.Delimiter.Read(Delimiter),
            LastUpdated is { } updated ? updated : Maybe<DateTimeOffset>.None,
            LanguageCode.Read(Language));
    }
}

internal sealed record CardEntryDto
{
    public required string Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public IReadOnlyList<CardEntryPropertyDto>? Properties { get; init; }

    public IReadOnlyList<string>? Segments { get; init; }

    internal CardEntry ToModel() => new(
        CardId.From(Id),
        CreatedAt,
        UpdatedAt,
        [.. (Properties ?? []).Select(property => property.ToModel())],
        Segments is { } segments ? Maybe.From(segments) : Maybe<IReadOnlyList<string>>.None);
}

internal sealed record CardFacetBucketDto(JsonElement Value, long Count)
{
    internal CardFacetBucket ToModel() =>
        new(Value.ValueKind == JsonValueKind.String ? Value.GetString()! : Value.GetRawText(), Count);
}

internal sealed record CardFacetDto(string Property, IReadOnlyList<CardFacetBucketDto>? Items, string? After, long TotalCount)
{
    internal CardFacetResult ToModel() => new(
        CardPropertyId.Read(Property),
        [.. (Items ?? []).Select(bucket => bucket.ToModel())],
        After is { Length: > 0 } after ? PageCursor.From(after) : Maybe<PageCursor>.None,
        TotalCount);
}

internal sealed record CardEntriesDto
{
    public required CardEntryDto[] Items { get; init; }

    public string? After { get; init; }

    public long? TotalCount { get; init; }

    public IReadOnlyList<CardFacetDto>? Facets { get; init; }

    internal CardSearchResult ToModel() => new(
        Pages.ToPage(Items, After, TotalCount, entry => entry.ToModel()),
        [.. (Facets ?? []).Select(facet => facet.ToModel())]);
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    RespectNullableAnnotations = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SearchCardEntriesDto))]
[JsonSerializable(typeof(CardEntriesDto))]
internal sealed partial class CardEntriesJsonContext : JsonSerializerContext;
