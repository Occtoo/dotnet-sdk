using System.Net;
using System.Text.Json;
using Occtoo.Authentication;
using Occtoo.Cards;
using Occtoo.Sources;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Cards;

public class SegmentsClientTests
{
    private const string SegmentBody = """
        {
          "id": "summer26", "segmentDefinitionId": "campaigns", "cardId": "products", "type": "Query", "status": "Initializing",
          "name": "Summer 26", "description": null, "language": "en", "image": null, "tags": ["summer"],
          "where": { "and": [ { "text": { "property": "season", "eq": "summer" } }, { "integer": { "property": "stock", "gte": 1 } } ] },
          "startTime": null, "endTime": "2026-09-01T00:00:00Z",
          "createdAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-01T10:00:00Z"
        }
        """;

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions { Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")) });

    [Fact]
    public async Task Get_reads_the_stored_where_back_into_a_condition()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, SegmentBody);
        using var client = Client(handler);

        var segment = (await client.Segments.Get("products", "summer26", TestContext.Current.CancellationToken)).Value;

        segment.Status.GetValueOrThrow().ShouldBe(SegmentStatus.Initializing);
        segment.DefinitionId.Value.ShouldBe("campaigns");
        segment.Where.GetValueOrThrow().ToString()
            .ShouldBe("""{"and":[{"text":{"property":"season","eq":"summer"}},{"integer":{"property":"stock","gte":1}}]}""");
        segment.StartTime.HasNoValue.ShouldBeTrue();
        segment.EndTime.HasValue.ShouldBeTrue();
        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/card-definitions/products/segments/summer26");
    }

    [Fact]
    public async Task A_fixed_segment_sends_its_cards_as_an_id_condition()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Created, SegmentBody);
        using var client = Client(handler);

        (await client.Segments.Create("products",
            CreateSegment.Fixed("featured", "Featured", LanguageCode.From("en"), "chair-1", "table-2").Tagged("home"),
            TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        handler.Requests.Single().Body.ShouldBe(
            """{"id":"featured","name":"Featured","language":"en","where":{"text":{"property":"id","in":["chair-1","table-2"]}},"type":"Fixed","tags":["home"]}""");
    }

    [Fact]
    public async Task Update_sends_only_what_changed()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, SegmentBody).Respond(HttpStatusCode.OK, SegmentBody);
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var segment = (await client.Segments.Get("products", "summer26", ct)).Value;
        (await client.Segments.Update(segment.Edit().WithWhere(Where.Boolean("onSale").Eq(true)).WithImage("sun"), ct)).IsSuccess.ShouldBeTrue();

        var request = handler.Requests[1];
        request.Method.ShouldBe(HttpMethod.Patch);
        request.Body.ShouldBe("""{"where":{"boolean":{"property":"onSale","eq":true}},"image":"sun"}""");
    }

    [Fact]
    public async Task List_carries_its_filters_and_delete_archives()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, $$"""{ "items": [{{SegmentBody}}], "after": null, "totalCount": null }""")
            .Respond(HttpStatusCode.NoContent, "");
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var page = (await client.Segments.List("products", new SegmentListQuery { Type = SegmentType.Query, Status = SegmentStatus.Active }, ct)).Value;
        (await client.Segments.Delete("products", "summer26", ct)).IsSuccess.ShouldBeTrue();

        page.Items.ShouldHaveSingleItem();
        handler.Requests[0].RequestUri!.Query.ShouldBe("?type=Query&status=Active&limit=50");
        handler.Requests[1].Method.ShouldBe(HttpMethod.Delete);
    }

    [Fact]
    public void An_image_key_is_lowercase_letters_only()
    {
        Should.Throw<ArgumentException>(() => CreateSegment.Query("s", "S", LanguageCode.From("en"), Where.HasValue("title")).WithImage("Sun1"));
    }
}
