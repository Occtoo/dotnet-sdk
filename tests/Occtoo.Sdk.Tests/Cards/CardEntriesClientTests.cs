using System.Net;
using Occtoo.Authentication;
using Occtoo.Cards;
using Occtoo.Sources;
using Shouldly;
using Xunit;
using SortDirection = Occtoo.Cards.SortDirection;

namespace Occtoo.Sdk.Tests.Cards;

public class CardEntriesClientTests
{
    private const string EntriesBody = """
        {
          "items": [{
            "id": "chair-1", "createdAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-02T10:00:00Z",
            "properties": [
              { "id": "title", "value": "Chair", "type": "LocalizedText", "delimiter": null, "lastUpdated": "2026-11-02T10:00:00Z", "language": "en" },
              { "id": "stock", "value": 12, "type": "Integer", "delimiter": null, "lastUpdated": null, "language": null },
              { "id": "colors", "value": ["red", "blue"], "type": "List", "delimiter": ",", "lastUpdated": null, "language": null }
            ],
            "lifecycles": [], "segments": ["campaigns/summer26"]
          }],
          "after": "next-page", "totalCount": 42, "lifecycleStates": [],
          "facets": [{ "property": "brand", "items": [{ "value": "Acme", "count": 30 }, { "value": 7, "count": 2 }], "after": null, "totalCount": 2 }]
        }
        """;

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions { Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")) });

    [Fact]
    public async Task Search_sends_the_query_and_types_the_values()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, EntriesBody);
        using var client = Client(handler);

        var result = (await client.CardEntries.Search("products",
            CardSearch.Matching(Where.Integer("stock").Gt(0), LanguageCode.From("en"))
                .Select("title", "stock", "colors")
                .OrderBy("stock", SortDirection.Descending)
                .FacetOn(CardFacet.On("brand").SortedBy(FacetOrder.Count).Take(10))
                .WithSegments()
                .WithTotal()
                .Take(10),
            TestContext.Current.CancellationToken)).Value;

        var entry = result.Entries.Items.ShouldHaveSingleItem();
        entry.Values[0].Value.ShouldBe(PropertyValue.Text("Chair"));
        entry.Values[0].Language.GetValueOrThrow().Value.ShouldBe("en");
        entry.Values[1].Value.ShouldBe(PropertyValue.Integer(12));
        entry.Values[2].Value.ShouldBeOfType<PropertyValue.ListValue>();
        entry.Segments.GetValueOrThrow().ShouldBe(["campaigns/summer26"]);
        result.Entries.Next.GetValueOrThrow().Value.ShouldBe("next-page");
        result.Entries.Total.GetValueOrThrow().ShouldBe(42);
        result.Facets.ShouldHaveSingleItem().Values.Select(bucket => bucket.Value).ShouldBe(["Acme", "7"]);

        var request = handler.Requests.Single();
        request.RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/card-definitions/products/entries/search");
        request.Body.ShouldBe(
            """{"where":{"integer":{"property":"stock","gt":0}},"language":"en","sort":[{"property":"stock","direction":"Descending"}],"properties":["title","stock","colors"],"limit":10,"includeEntries":true,"includeTotalCount":true,"includeSegments":true,"facets":[{"property":"brand","ignoreCase":false,"sort":{"by":"Count","direction":"Descending"},"first":10}]}""");
    }

    [Fact]
    public async Task SearchRelated_targets_the_relation_route()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """{ "items": [], "after": null, "totalCount": null, "lifecycleStates": [], "facets": [] }""");
        using var client = Client(handler);

        var result = await client.CardEntries.SearchRelated("products", "chair-1", "productVariants", CardSearch.All(), TestContext.Current.CancellationToken);

        result.Value.Entries.Items.ShouldBeEmpty();
        result.Value.Entries.HasMore.ShouldBeFalse();
        handler.Requests.Single().RequestUri!.AbsolutePath.ShouldBe("/v1/card-definitions/products/entries/chair-1/relations/productVariants/search");
    }

    [Fact]
    public void A_page_holds_one_to_a_hundred_entries()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CardSearch.All().Take(101));
    }
}
