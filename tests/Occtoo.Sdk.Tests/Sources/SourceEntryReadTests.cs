using System.Net;
using CSharpFunctionalExtensions;
using Occtoo.Authentication;
using Occtoo.Sources;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Sources;

public class SourceEntryReadTests
{
    private const string EntryBody = """
        {
          "id": "chair-1",
          "properties": [
            { "id": "price", "value": 42.5, "type": "Decimal", "delimiter": null, "lastUpdated": "2026-09-15T10:00:00Z", "language": null },
            { "id": "stock", "value": 7, "type": "Integer", "delimiter": null, "lastUpdated": "2026-09-15T10:00:00Z", "language": null },
            { "id": "name", "value": "Blue chair", "type": "LocalizedText", "delimiter": null, "lastUpdated": "2026-09-15T10:01:00Z", "language": "en" },
            { "id": "name", "value": "Blå stol", "type": "LocalizedText", "delimiter": null, "lastUpdated": "2026-09-15T10:01:00Z", "language": "sv" },
            { "id": "tags", "value": ["summer", "sale"], "type": "List", "delimiter": ",", "lastUpdated": "2026-09-15T10:02:00Z", "language": null },
            { "id": "inStock", "value": true, "type": "Boolean", "delimiter": null, "lastUpdated": "2026-09-15T10:02:00Z", "language": null },
            { "id": "publishedAt", "value": "2026-01-01T00:00:00Z", "type": "Timestamp", "delimiter": null, "lastUpdated": "2026-09-15T10:02:00Z", "language": null },
            { "id": "cleared", "value": null, "type": "Text", "delimiter": null, "lastUpdated": "2026-09-15T10:03:00Z", "language": null },
            { "id": "legacy", "value": "123", "type": "Wildcard", "delimiter": null, "lastUpdated": "2026-09-15T10:03:00Z", "language": null }
          ],
          "lastUpdated": "2026-09-15T10:03:00Z"
        }
        """;

    private static readonly SourceId Products = SourceId.From("products");

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

    [Fact]
    public async Task GetEntry_types_every_value_by_the_configured_property_type()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, EntryBody);
        using var client = Client(handler);

        var result = await client.Sources.GetEntry(Products, EntryId.From("chair-1"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var entry = result.Value;
        entry.Id.Value.ShouldBe("chair-1");
        entry.LastUpdated.ShouldBe(DateTimeOffset.Parse("2026-09-15T10:03:00Z", null));

        var byId = entry.Properties.ToLookup(property => property.Id.Value);
        byId["price"].Single().Value.ShouldBe(PropertyValue.Decimal(42.5m));
        byId["stock"].Single().Value.ShouldBe(PropertyValue.Integer(7));
        byId["name"].Select(p => p.Language.GetValueOrThrow().Value).ShouldBe(["en", "sv"]);
        byId["name"].First().Value.ShouldBe(PropertyValue.Text("Blue chair"));
        byId["tags"].Single().Value.ShouldBeOfType<PropertyValue.ListValue>().Items.ShouldBe(["summer", "sale"]);
        byId["tags"].Single().Delimiter.GetValueOrThrow().Value.ShouldBe(",");
        byId["instock"].Single().Value.ShouldBe(PropertyValue.Boolean(true));
        byId["publishedat"].Single().Value.ShouldBe(PropertyValue.Timestamp(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null)));
        byId["cleared"].Single().Value.ShouldBe(PropertyValue.Clear);
        byId["legacy"].Single().Type.HasNoValue.ShouldBeTrue();
        byId["legacy"].Single().Value.ShouldBe(PropertyValue.Text("123"));

        handler.Requests.Single().RequestUri!.AbsoluteUri
            .ShouldBe("https://api.occtoo.com/v1/sources/products/entries/chair-1");
    }

    [Fact]
    public async Task GetEntries_requests_the_ids_in_order_and_returns_what_exists()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, $$"""{ "items": [{{EntryBody}}] }""");
        using var client = Client(handler);

        var result = await client.Sources.GetEntries(
            Products, [EntryId.From("chair-1"), EntryId.From("missing/one")], TestContext.Current.CancellationToken);

        result.Value.ShouldHaveSingleItem().Id.Value.ShouldBe("chair-1");
        handler.Requests.Single().RequestUri!.AbsoluteUri
            .ShouldBe("https://api.occtoo.com/v1/sources/products/entries?id=chair-1&id=missing%2Fone");
    }

    [Fact]
    public async Task GetEntries_rejects_an_empty_or_oversized_id_list_without_a_request()
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        var none = await client.Sources.GetEntries(Products, [], TestContext.Current.CancellationToken);
        none.Error.ShouldBeOfType<ValidationError>();

        var tooMany = await client.Sources.GetEntries(
            Products,
            [.. Enumerable.Range(0, SourcesClient.MaxEntriesPerRead + 1).Select(i => EntryId.From($"e{i}"))],
            TestContext.Current.CancellationToken);
        tooMany.Error.ShouldBeOfType<ValidationError>();

        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_unrepresentable_stored_value_is_a_conflict_and_a_missing_entry_is_not_found()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Conflict, """{ "title": "Stored property 'price' cannot be represented as Decimal." }""")
            .Respond(HttpStatusCode.NotFound, """{ "title": "Source entry not found." }""");
        using var client = Client(handler);

        var conflict = await client.Sources.GetEntry(Products, EntryId.From("chair-1"), TestContext.Current.CancellationToken);
        conflict.Error.ShouldBeOfType<ConflictError>();

        var missing = await client.Sources.GetEntry(Products, EntryId.From("gone"), TestContext.Current.CancellationToken);
        missing.Error.ShouldBeOfType<NotFoundError>();
    }

    [Fact]
    public async Task An_incomplete_entry_is_an_unexpected_error_not_an_exception()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, "{}");
        using var client = Client(handler);

        var result = await client.Sources.GetEntry(Products, EntryId.From("chair-1"), TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Theory]
    [InlineData("""{ "items": [null] }""")]
    [InlineData("""{ "items": [{ "id": "chair-1", "properties": [null], "lastUpdated": "2026-09-15T10:00:00Z" }] }""")]
    public async Task Null_elements_in_an_entry_batch_are_unexpected_errors(string body)
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, body);
        using var client = Client(handler);

        var result = await client.Sources.GetEntries(Products, [EntryId.From("chair-1")], TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Theory]
    [InlineData("""1e300""", "Decimal", "outside the range")]
    [InlineData("""["a", 1]""", "List", "not a string")]
    [InlineData("""{ "nested": true }""", "Text", "no property type represents")]
    public async Task A_value_the_sdk_cannot_represent_is_a_data_type_error_not_a_silent_loss(string value, string type, string reason)
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, $$"""
            { "id": "chair-1", "lastUpdated": "2026-09-15T10:00:00Z",
              "properties": [{ "id": "p", "value": {{value}}, "type": "{{type}}", "lastUpdated": "2026-09-15T10:00:00Z" }] }
            """);
        using var client = Client(handler);

        var result = await client.Sources.GetEntry(Products, EntryId.From("chair-1"), TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<DataTypeError>().Message.ShouldContain(reason);
    }
}
