using System.Net;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Occtoo.Authentication;
using Occtoo.Sources;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Sources;

public class SourceManagementTests
{
    private const string SourceBody = """
        {
          "id": "products",
          "name": "Products",
          "description": null,
          "status": "Active",
          "type": "Media",
          "createdAt": "2026-09-01T10:00:00Z",
          "updatedAt": "2026-09-02T11:30:00Z"
        }
        """;

    private const string PropertyBody = """
        { "id": "tags", "displayName": "Tags", "description": "Marketing tags", "type": "List", "delimiter": ",", "state": "Active" }
        """;

    private static readonly SourceId Products = SourceId.From("products");

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

    [Fact]
    public async Task List_carries_filters_in_the_query_and_maps_the_page()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, $$"""
            { "items": [{{SourceBody}}], "after": null, "totalCount": null }
            """);
        using var client = Client(handler);

        var result = await client.Sources.List(new SourceListQuery
        {
            Name = "prod",
            Type = SourceType.Generic,
            Status = SourceStatus.Active,
            UpdatedFrom = DateTimeOffset.Parse("2026-09-01T00:00:00Z", null),
        }, TestContext.Current.CancellationToken);

        var page = result.Value;
        var source = page.Items.ShouldHaveSingleItem();
        source.Id.ShouldBe(Products);
        source.Description.HasNoValue.ShouldBeTrue();
        source.Status.ShouldBe(SourceStatus.Active);
        source.Type.ShouldBe(SourceType.Media);
        page.Next.HasNoValue.ShouldBeTrue();
        page.HasMore.ShouldBeFalse();

        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe(
            "https://api.occtoo.com/v1/sources?name=prod&type=Generic&status=Active"
            + "&updatedFrom=2026-09-01T00%3A00%3A00.0000000Z&limit=50");
    }

    [Fact]
    public async Task Get_reads_one_source()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, SourceBody);
        using var client = Client(handler);

        var result = await client.Sources.Get(Products, TestContext.Current.CancellationToken);

        result.Value.Name.ShouldBe("Products");
        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/sources/products");
    }

    [Fact]
    public async Task Create_posts_the_source()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Created, SourceBody);
        using var client = Client(handler);

        var result = await client.Sources.Create(
            new CreateSource(Products, "Products") { Description = "Catalog" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/sources");
        request.Body.ShouldBe("""{"id":"products","name":"Products","description":"Catalog"}""");
    }

    [Fact]
    public async Task Update_patches_only_the_given_fields()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, SourceBody);
        using var client = Client(handler);

        await client.Sources.Update(
            Products,
            new UpdateSource { Description = "" },
            TestContext.Current.CancellationToken);

        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Patch);
        request.Body.ShouldBe("""{"description":""}""");
    }

    [Fact]
    public async Task Delete_succeeds_when_the_cleanup_is_accepted()
    {
        using var handler = new StubHandler().Respond(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var client = Client(handler);

        var result = await client.Sources.Delete(Products, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.Requests.Single().Method.ShouldBe(HttpMethod.Delete);
    }

    [Fact]
    public async Task ListProperties_maps_typed_and_untyped_properties()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, $$"""
            {
              "items": [
                {{PropertyBody}},
                { "id": "legacy", "displayName": "Legacy", "description": null, "type": null, "delimiter": null, "state": "Deleting" },
                { "id": "wild", "displayName": "Wild", "description": null, "type": "Wildcard", "delimiter": null, "state": "Active" }
              ],
              "after": "next",
              "totalCount": null
            }
            """);
        using var client = Client(handler);

        var result = await client.Sources.ListProperties(
            Products, new PageRequest { Limit = 10 }, TestContext.Current.CancellationToken);

        var page = result.Value;
        page.Items.Count.ShouldBe(3);
        page.Items[0].Type.GetValueOrThrow().ShouldBe(SourcePropertyType.List);
        page.Items[0].Delimiter.GetValueOrThrow().Value.ShouldBe(",");
        page.Items[1].Type.HasNoValue.ShouldBeTrue();
        page.Items[1].State.ShouldBe(SourcePropertyState.Deleting);
        page.Items[2].Type.HasNoValue.ShouldBeTrue(); // a type name this SDK does not know reads as untyped
        page.Next.GetValueOrThrow().Value.ShouldBe("next");
        page.HasMore.ShouldBeTrue();

        handler.Requests.Single().RequestUri!.AbsoluteUri
            .ShouldBe("https://api.occtoo.com/v1/sources/products/properties?limit=10");
    }

    [Fact]
    public async Task UpsertProperty_puts_the_metadata_with_the_type_as_its_name()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, PropertyBody);
        using var client = Client(handler);

        var result = await client.Sources.UpsertProperty(
            Products,
            PropertyId.From("tags"),
            new UpsertSourceProperty("Tags") { Type = SourcePropertyType.List, Delimiter = Delimiter.From(",") },
            TestContext.Current.CancellationToken);

        result.Value.Id.Value.ShouldBe("tags");
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Put);
        request.RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/sources/products/properties/tags");
        request.Body.ShouldBe("""{"displayName":"Tags","type":"List","delimiter":","}""");
    }

    [Fact]
    public async Task DeleteProperty_and_GetProperty_target_the_property_route()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, PropertyBody)
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var client = Client(handler);

        var property = await client.Sources.GetProperty(Products, PropertyId.From("tags"), TestContext.Current.CancellationToken);
        property.Value.DisplayName.ShouldBe("Tags");

        var deleted = await client.Sources.DeleteProperty(Products, PropertyId.From("tags"), TestContext.Current.CancellationToken);
        deleted.IsSuccess.ShouldBeTrue();
        handler.Requests[1].Method.ShouldBe(HttpMethod.Delete);
    }

    [Fact]
    public async Task A_rejection_classifies_like_every_other_surface()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.NotFound, """{ "title": "no such source" }""");
        using var client = Client(handler);

        var result = await client.Sources.Get(Products, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NotFoundError>().Message.ShouldContain("no such source");
    }

    [Fact]
    public async Task Totals_are_requested_only_when_asked_for()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, """{ "items": [], "after": null, "totalCount": 42 }""");
        using var client = Client(handler);

        var result = await client.Sources.ListProperties(
            Products, new PageRequest { IncludeTotal = true }, TestContext.Current.CancellationToken);

        result.Value.Total.GetValueOrThrow().ShouldBe(42);
        handler.Requests.Single().RequestUri!.Query.ShouldBe("?limit=50&includeTotalCount=true");
    }

    [Fact]
    public async Task A_list_type_without_a_delimiter_and_a_scalar_with_one_fail_before_the_request()
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        var missing = await client.Sources.UpsertProperty(Products, PropertyId.From("tags"),
            new UpsertSourceProperty("Tags") { Type = SourcePropertyType.LocalizedList }, TestContext.Current.CancellationToken);
        missing.Error.ShouldBeOfType<ValidationError>();

        var stray = await client.Sources.UpsertProperty(Products, PropertyId.From("price"),
            new UpsertSourceProperty("Price") { Type = SourcePropertyType.Decimal, Delimiter = Delimiter.From(",") },
            TestContext.Current.CancellationToken);
        stray.Error.ShouldBeOfType<ValidationError>();

        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_incomplete_source_is_an_unexpected_error_not_an_exception()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, "{}");
        using var client = Client(handler);

        var result = await client.Sources.Get(Products, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }
}
