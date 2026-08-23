using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Occtoo.Sources;
using Occtoo.Sources.Internal;

namespace Occtoo.Sdk.Benchmarks;

/// <summary>
/// Races the three ways of serializing an ingest batch:
/// <list type="bullet">
/// <item><b>DtoMirror</b> — map the public model into a mirror DTO tree, then
/// let source-gen serialize it (the SDK's original approach, replicated
/// verbatim). Costs a duplicate object graph and a second traversal.</item>
/// <item><b>WholeBodyConverter</b> — one converter writes the entire request.
/// Fastest to traverse, but a converter call is not resumable, so the whole
/// payload accumulates in the writer's buffer — Large Object Heap churn at
/// 1000 entries.</item>
/// <item><b>EntryConverter</b> — what the SDK does now: source-gen owns the
/// body and the array (giving the serializer flush points between elements),
/// a converter writes each ~300-byte entry.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class IngestSerializationBenchmarks
{
    // Identifiers hoisted the way a real bulk-ingest caller should hoist them,
    // so the benchmark measures serialization, not value-object construction.
    private static readonly PropertyId Name = PropertyId.From("name");
    private static readonly PropertyId Price = PropertyId.From("price");
    private static readonly PropertyId InStock = PropertyId.From("inStock");
    private static readonly PropertyId PublishedAt = PropertyId.From("publishedAt");
    private static readonly PropertyId Tags = PropertyId.From("tags");
    private static readonly LanguageCode English = LanguageCode.From("en");
    private static readonly LanguageCode Swedish = LanguageCode.From("sv-SE");

    private SourceEntry[] _entries = [];

    [Params(100, 1000)]
    public int EntryCount { get; set; }

    [GlobalSetup]
    public void Setup() =>
        _entries = [.. Enumerable.Range(0, EntryCount).Select(i =>
            SourceEntry.WithId($"sku-{i}")
                .WithProperty(new(Name, PropertyValue.Text("Blue chair"), English))
                .WithProperty(new(Name, PropertyValue.Text("Blå stol"), Swedish))
                .WithProperty(new(Price, PropertyValue.Decimal(100.111m)))
                .WithProperty(new(InStock, PropertyValue.Boolean(true)))
                .WithProperty(new(PublishedAt, PropertyValue.Timestamp(DateTimeOffset.UnixEpoch)))
                .WithProperty(new(Tags, PropertyValue.List("summer", "sale")))
                .Build())];

    // All three serialize the way the SDK does in production: JsonContent
    // drives SerializeAsync against the transport stream, which flushes the
    // writer at resumable points. A sync Serialize(Utf8JsonWriter, ...) harness
    // would never flush and misrepresent the memory behaviour.

    [Benchmark(Baseline = true)]
    public Task DtoMirror() =>
        JsonSerializer.SerializeAsync(
            Stream.Null,
            MapToDto(_entries),
            OldIngestJsonContext.Default.OldRequestDto);

    [Benchmark]
    public Task WholeBodyConverter() =>
        JsonSerializer.SerializeAsync(
            Stream.Null,
            new MonolithicRequestBody(_entries),
            MonolithicJsonContext.Default.MonolithicRequestBody);

    [Benchmark]
    public Task EntryConverter() =>
        JsonSerializer.SerializeAsync(
            Stream.Null,
            new IngestRequestBody(_entries),
            IngestJsonContext.Default.IngestRequestBody);

    // ── The SDK's original DTO-mirror path, replicated verbatim ─────────────

    private static OldRequestDto MapToDto(IReadOnlyCollection<SourceEntry> entries) =>
        new([
            .. entries.Select(entry => new OldEntryDto(
                entry.Id.Value,
                [
                    .. entry.Properties.Select(property => new OldPropertyDto(
                        property.Id.Value,
                        property.Value,
                        property.Language.HasValue ? property.Language.Value.Value : null)),
                ])),
        ]);
}

public sealed record OldRequestDto(IReadOnlyList<OldEntryDto> Entries);

public sealed record OldEntryDto(string Id, IReadOnlyList<OldPropertyDto> Properties);

public sealed record OldPropertyDto(
    string Id,
    [property: JsonConverter(typeof(OldPropertyValueJsonConverter))] PropertyValue Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Language);

public sealed class OldPropertyValueJsonConverter : JsonConverter<PropertyValue>
{
    public override void Write(Utf8JsonWriter writer, PropertyValue value, JsonSerializerOptions options) =>
        SourceEntryJsonConverter.WritePropertyValue(writer, value);

    public override PropertyValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(OldRequestDto))]
public sealed partial class OldIngestJsonContext : JsonSerializerContext;

// ── The whole-body converter variant, kept for the memory comparison ────────

[JsonConverter(typeof(MonolithicRequestJsonConverter))]
public sealed record MonolithicRequestBody(IReadOnlyCollection<SourceEntry> Entries);

public sealed class MonolithicRequestJsonConverter : JsonConverter<MonolithicRequestBody>
{
    private static readonly SourceEntryJsonConverter EntryConverter = new();

    public override void Write(Utf8JsonWriter writer, MonolithicRequestBody body, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("entries"u8);
        foreach (var entry in body.Entries)
            EntryConverter.Write(writer, entry, options);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public override MonolithicRequestBody Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MonolithicRequestBody))]
public sealed partial class MonolithicJsonContext : JsonSerializerContext;
