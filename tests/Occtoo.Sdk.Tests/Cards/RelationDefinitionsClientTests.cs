using System.Net;
using Occtoo.Authentication;
using Occtoo.Cards;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Cards;

public class RelationDefinitionsClientTests
{
    private const string RelationBody = """
        {
          "id": "productVariants", "name": "Product variants", "description": null, "tags": [],
          "sourceCardId": "products", "targetCardId": "variants",
          "match": { "sourceProperty": "sku", "targetProperty": "parentSku" },
          "sourceDisplayName": "Variants", "targetDisplayName": "Variant of",
          "order": { "type": "BasedOnSourceProperty", "property": "variantSkus", "targetMatchProperty": null },
          "status": "Initializing", "createdAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-01T10:00:00Z"
        }
        """;

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions { Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")) });

    [Fact]
    public async Task Create_sends_both_sides_the_labels_and_the_order()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Created, RelationBody);
        using var client = Client(handler);

        var created = (await client.RelationDefinitions.Create(
            CreateRelationDefinition.WithId("productVariants", "Product variants")
                .From("products", "sku")
                .To("variants", "parentSku")
                .Labelled("Variants", "Variant of")
                .OrderedBy(RelationOrder.BySourceList("variantSkus")),
            TestContext.Current.CancellationToken)).Value;

        created.Status.GetValueOrThrow().ShouldBe(RelationDefinitionStatus.Initializing);
        created.Order.GetValueOrThrow().Basis.GetValueOrThrow().ShouldBe(RelationOrderBasis.SourceList);
        handler.Requests.Single().Body.ShouldBe(
            """{"id":"productVariants","name":"Product variants","sourceCardId":"products","targetCardId":"variants","match":{"sourceProperty":"sku","targetProperty":"parentSku"},"sourceDisplayName":"Variants","targetDisplayName":"Variant of","order":{"type":"BasedOnSourceProperty","property":"variantSkus"}}""");
    }

    [Fact]
    public async Task Update_sends_a_new_match_and_list_carries_its_filters()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, RelationBody)
            .Respond(HttpStatusCode.OK, $$"""{ "items": [{{RelationBody}}], "after": null, "totalCount": null }""");
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var relation = new RelationDefinitionListQuery { SourceCardDefinitionId = Occtoo.Events.CardDefinitionId.From("products") };
        (await client.RelationDefinitions.Update(
            CreateRelationDefinitionsEdit("productVariants").WithMatch("id", "parentSku"), ct)).IsSuccess.ShouldBeTrue();
        (await client.RelationDefinitions.List(relation, ct)).Value.Items.ShouldHaveSingleItem();

        handler.Requests[0].Method.ShouldBe(HttpMethod.Patch);
        handler.Requests[0].RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/relation-definitions/productVariants");
        handler.Requests[0].Body.ShouldBe("""{"match":{"sourceProperty":"id","targetProperty":"parentSku"}}""");
        handler.Requests[1].RequestUri!.Query.ShouldBe("?sourceCardId=products&limit=50");
    }

    [Fact]
    public async Task A_relation_in_use_cannot_be_deleted()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Conflict, """{ "detail": "used by a segment" }""");
        using var client = Client(handler);

        var result = await client.RelationDefinitions.Delete("productVariants", TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ConflictError>();
    }

    private static UpdateRelationDefinition CreateRelationDefinitionsEdit(string id) =>
        new RelationDefinition(id, "x", default, [], "products", "variants", new RelationMatch("sku", "parentSku"),
            default, default, default, default, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch).Edit();
}
