using System.Net;
using System.Text.Json;
using Occtoo.Authentication;
using Occtoo.Cards;
using Occtoo.Events;
using Occtoo.Sources;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Cards;

public class CardDefinitionsClientTests
{
    private const string DefinitionBody = """
        {
          "id": "products", "name": "Products", "description": null, "tags": ["core"], "status": "Active", "etag": 3,
          "displayOptions": { "name": "title", "image": "heroImage" },
          "propertyGroups": [ { "name": "Basics", "propertyIds": ["title"] } ],
          "properties": [
            { "id": "title", "sourceId": "products", "sourcePropertyId": "title", "type": "LocalizedText",
              "name": { "en": "Title", "EN_legacy": "Titel" }, "description": {}, "settings": {}, "delimiter": null,
              "order": 1, "isHidden": false, "isEditable": true, "managedTagList": null }
          ],
          "pendingChanges": {
            "set": [ { "id": "tags", "sourceId": "products", "sourcePropertyId": "tags", "type": "List", "delimiter": ",", "order": 0, "isHidden": false, "isEditable": false } ],
            "remove": []
          },
          "createdAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-02T10:00:00Z"
        }
        """;

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions { Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")) });

    [Fact]
    public async Task Get_maps_the_display_groups_properties_and_pending_changes()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, DefinitionBody);
        using var client = Client(handler);

        var definition = (await client.CardDefinitions.Get("products", TestContext.Current.CancellationToken)).Value;

        definition.ETag.Value.ShouldBe(3u);
        definition.Display.Name.GetValueOrThrow().Value.ShouldBe("title");
        definition.Display.Description.HasNoValue.ShouldBeTrue();
        definition.PropertyGroups.ShouldHaveSingleItem().PropertyIds.ShouldBe([CardPropertyId.From("title")]);
        var title = definition.Properties.ShouldHaveSingleItem();
        title.Type.GetValueOrThrow().ShouldBe(SourcePropertyType.LocalizedText);
        title.Name.Keys.Select(code => code.Value).ShouldBe(["en", "EN_legacy"], ignoreOrder: true);
        definition.PendingChanges.GetValueOrThrow().Set.ShouldHaveSingleItem().Delimiter.GetValueOrThrow().Value.ShouldBe(",");
        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/card-definitions/products");
    }

    [Fact]
    public async Task Create_sends_the_draft_with_its_card_properties()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Created, DefinitionBody);
        using var client = Client(handler);

        var draft = CreateCardDefinition.WithId("products", "Products")
            .Tagged("core")
            .DisplayedAs(new CardDisplay(CardPropertyId.From("title"), default, default))
            .WithProperty(CardProperty.Of("title", "products", "title", SourcePropertyType.LocalizedText).Named(LanguageCode.From("en"), "Title"))
            .WithProperty(CardProperty.Of("tags", "products", "tags", SourcePropertyType.List).WithDelimiter(Delimiter.From(",")).Hidden());
        (await client.CardDefinitions.Create(draft, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        var body = JsonDocument.Parse(handler.Requests.Single().Body!).RootElement;
        body.GetProperty("id").GetString().ShouldBe("products");
        body.GetProperty("displayOptions").GetProperty("name").GetString().ShouldBe("title");
        var properties = body.GetProperty("properties");
        properties.GetArrayLength().ShouldBe(2);
        properties[0].GetProperty("name").GetProperty("en").GetString().ShouldBe("Title");
        properties[1].GetProperty("delimiter").GetString().ShouldBe(",");
        properties[1].GetProperty("isHidden").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Update_sends_if_match_and_keeps_the_pending_changes_already_saved()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, DefinitionBody)
            .Respond(HttpStatusCode.OK, DefinitionBody);
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var definition = (await client.CardDefinitions.Get("products", ct)).Value;
        var changes = definition.Edit()
            .WithName("Catalogue")
            .SetProperty(CardProperty.Of("price", "products", "price", SourcePropertyType.Decimal))
            .RemoveProperty("title");
        (await client.CardDefinitions.Update(changes, ct)).IsSuccess.ShouldBeTrue();

        var request = handler.Requests[1];
        request.Method.ShouldBe(HttpMethod.Patch);
        request.Header("If-Match").ShouldBe("\"3\"");
        var body = JsonDocument.Parse(request.Body!).RootElement;
        body.GetProperty("name").GetString().ShouldBe("Catalogue");
        body.TryGetProperty("tags", out _).ShouldBeFalse();
        var pending = body.GetProperty("pendingChanges");
        pending.GetProperty("set").EnumerateArray().Select(property => property.GetProperty("id").GetString()).ShouldBe(["tags", "price"]);
        pending.GetProperty("remove").EnumerateArray().Select(id => id.GetString()).ShouldBe(["title"]);
    }

    [Fact]
    public async Task A_detail_only_change_leaves_the_pending_changes_out()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, DefinitionBody).Respond(HttpStatusCode.OK, DefinitionBody);
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var definition = (await client.CardDefinitions.Get("products", ct)).Value;
        (await client.CardDefinitions.Update(definition.Edit().WithDescription(""), ct)).IsSuccess.ShouldBeTrue();

        handler.Requests[1].Body.ShouldBe("""{"description":""}""");
    }

    [Fact]
    public async Task A_stale_etag_is_a_conflict()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.PreconditionFailed, """{ "detail": "The resource has changed." }""");
        using var client = Client(handler);

        var result = await client.CardDefinitions.Activate("products", ETag.From(2), TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ConflictError>().Message.ShouldContain("read it again");
        var request = handler.Requests.Single();
        request.RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/card-definitions/products/activations");
        request.Header("If-Match").ShouldBe("\"2\"");
    }

    [Fact]
    public async Task Delete_carries_the_etag()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.NoContent, "");
        using var client = Client(handler);

        (await client.CardDefinitions.Delete("products", ETag.From(7), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        handler.Requests.Single().Method.ShouldBe(HttpMethod.Delete);
        handler.Requests.Single().Header("If-Match").ShouldBe("\"7\"");
    }

    [Fact]
    public async Task An_export_sends_its_where_and_reads_back_the_file()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Accepted, """
                { "id": "6f2c25c1-0000-4000-8000-000000000001", "status": "Pending", "progress": null, "totalEntries": null, "processedEntries": null,
                  "createdAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-01T10:00:00Z", "expiresAt": "2026-11-08T10:00:00Z", "file": null }
                """)
            .Respond(HttpStatusCode.OK, """
                { "id": "6f2c25c1-0000-4000-8000-000000000001", "status": "Completed", "progress": 100, "totalEntries": 42, "processedEntries": 42,
                  "createdAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-01T10:05:00Z", "expiresAt": "2026-11-08T10:00:00Z",
                  "file": { "name": "products.csv", "sizeBytes": 2048, "url": "https://files.example.com/products.csv?sig=abc" } }
                """);
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var started = (await client.CardDefinitions.StartExport("products",
            StartCardExport.As(CardExportFormat.Csv).Matching(Where.Integer("stock").Gt(0)).WithProperties("title", "price"), ct)).Value;
        var done = (await client.CardDefinitions.GetExport("products", started.Id, ct)).Value;

        started.File.HasNoValue.ShouldBeTrue();
        done.Status.GetValueOrThrow().ShouldBe(CardExportStatus.Completed);
        done.File.GetValueOrThrow().Url.Host.ShouldBe("files.example.com");
        handler.Requests[0].Body.ShouldBe("""{"format":"CSV","where":{"integer":{"property":"stock","gt":0}},"properties":["title","price"],"includeTimestamp":false}""");
        handler.Requests[1].RequestUri!.AbsolutePath.ShouldBe("/v1/card-definitions/products/exports/6f2c25c1-0000-4000-8000-000000000001");
    }
}
