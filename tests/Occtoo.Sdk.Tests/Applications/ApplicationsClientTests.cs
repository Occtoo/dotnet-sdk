using System.Net;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Occtoo.Applications;
using Occtoo.Authentication;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Applications;

public class ApplicationsClientTests
{
    private const string ApplicationBody = """
        {
          "id": "2b1d7f3a-5c2e-4b8f-9a6d-1e0c4f7a8b9c",
          "name": "Catalog reader",
          "description": "Reads the product source configuration",
          "clientId": "client-abc",
          "tags": ["commerce"],
          "scopeKeys": ["read:sources"],
          "resourceSelectors": ["source:products"],
          "apiSelectors": [],
          "audiences": ["tenant-1"],
          "etag": 3,
          "createdAt": "2026-09-01T10:00:00Z",
          "lastModifiedAt": "2026-09-02T11:30:00Z"
        }
        """;

    private static readonly TenantApplicationId Id = TenantApplicationId.From(Guid.Parse("2b1d7f3a-5c2e-4b8f-9a6d-1e0c4f7a8b9c"));

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

    [Fact]
    public async Task Get_maps_the_application()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, ApplicationBody);
        using var client = Client(handler);

        var result = await client.Applications.Get(Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var application = result.Value;
        application.Id.ShouldBe(Id);
        application.Name.ShouldBe("Catalog reader");
        application.Description.GetValueOrDefault().ShouldBe("Reads the product source configuration");
        application.ClientId.Value.ShouldBe("client-abc");
        application.ScopeKeys.ShouldBe(["read:sources"]);
        application.ResourceSelectors.ShouldBe(["source:products"]);
        application.Etag.ShouldBe(3u);
        handler.Requests.Single().RequestUri!.AbsoluteUri
            .ShouldBe("https://api.occtoo.com/v1/applications/2b1d7f3a-5c2e-4b8f-9a6d-1e0c4f7a8b9c");
    }

    [Fact]
    public async Task List_carries_filters_and_paging_in_the_query_and_maps_the_page()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, $$"""
            { "items": [{{ApplicationBody}}], "after": "cursor-1", "totalCount": null }
            """);
        using var client = Client(handler);

        var result = await client.Applications.List(new ApplicationListQuery
        {
            Name = "catalog",
            Tags = ["partner", "production"],
            CreatedFrom = DateTimeOffset.Parse("2026-09-01T00:00:00+02:00", null),
            CreatedBy = Guid.Parse("9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a"),
            Page = new PageRequest { After = PageCursor.From("prev"), Limit = 25 },
        }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var page = result.Value;
        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Catalog reader");
        page.Next.GetValueOrThrow().Value.ShouldBe("cursor-1");
        page.Total.HasNoValue.ShouldBeTrue();

        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe(
            "https://api.occtoo.com/v1/applications?name=catalog&tags=partner&tags=production"
            + "&createdFrom=2026-08-31T22%3A00%3A00.0000000Z"
            + "&createdBy=9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a&after=prev&limit=25");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task List_rejects_limits_outside_the_api_range(int limit)
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        var result = await client.Applications.List(
            new ApplicationListQuery { Page = new PageRequest { Limit = limit } },
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>();
        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Create_posts_the_settings_and_returns_the_one_time_secret()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Created, $$"""
            { "application": {{ApplicationBody}}, "clientSecret": "s3cret" }
            """);
        using var client = Client(handler);

        var result = await client.Applications.Create(CreateApplication.WithName("Catalog reader")
            .WithDescription("Reads the product source configuration")
            .WithScopes("read:sources")
            .WithSources("products"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClientSecret.Value.ShouldBe("s3cret");
        result.Value.Application.Id.ShouldBe(Id);

        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("name").GetString().ShouldBe("Catalog reader");
        body.RootElement.GetProperty("scopeKeys")[0].GetString().ShouldBe("read:sources");
        body.RootElement.GetProperty("tags").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Update_puts_the_replacement_with_its_etag_and_a_stale_etag_is_a_conflict()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, ApplicationBody)
            .Respond(HttpStatusCode.Conflict, """{ "title": "stale etag" }""");
        using var client = Client(handler);

        UpdateApplication replacement = new Application(
            Id, "Catalog reader", Maybe<string>.None, ClientId.From("client-abc"), [], [], [], [], [], Etag: 3,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch).Edit().WithScopes("read:sources");

        var updated = await client.Applications.Update(Id, replacement, TestContext.Current.CancellationToken);
        updated.IsSuccess.ShouldBeTrue();
        handler.Requests[0].Method.ShouldBe(HttpMethod.Put);
        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        body.RootElement.GetProperty("etag").GetUInt32().ShouldBe(3u);

        var stale = await client.Applications.Update(Id, replacement, TestContext.Current.CancellationToken);
        stale.Error.ShouldBeOfType<ConflictError>();
    }

    [Fact]
    public async Task Delete_succeeds_on_no_content()
    {
        using var handler = new StubHandler().Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = Client(handler);

        var result = await client.Applications.Delete(Id, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.Requests.Single().Method.ShouldBe(HttpMethod.Delete);
    }

    [Fact]
    public async Task GetAccessCatalog_maps_the_nested_nodes()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            [
              {
                "key": "read:sources", "grantType": "Scope", "label": "Read sources", "description": null,
                "resourceId": null, "audience": null,
                "children": [
                  { "key": "source:products", "grantType": "Resource", "label": "products", "description": null,
                    "resourceId": "products", "audience": null, "children": [] }
                ]
              }
            ]
            """);
        using var client = Client(handler);

        var result = await client.Applications.GetAccessCatalog(TestContext.Current.CancellationToken);

        var scope = result.Value.ShouldHaveSingleItem();
        scope.Key.ShouldBe("read:sources");
        scope.Description.HasNoValue.ShouldBeTrue();
        scope.Children.ShouldHaveSingleItem().ResourceId.GetValueOrDefault().ShouldBe("products");
        handler.Requests.Single().RequestUri!.AbsoluteUri
            .ShouldBe("https://api.occtoo.com/v1/applications/access-catalog");
    }

    [Fact]
    public async Task An_incomplete_response_is_an_unexpected_error_not_an_exception()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, "{}");
        using var client = Client(handler);

        var result = await client.Applications.Get(Id, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }
}
