using System.Net;
using System.Text.Json;
using Occtoo.Authentication;
using Occtoo.Cards;
using Occtoo.Destinations;
using Occtoo.Sources;
using Shouldly;
using Xunit;
using SortDirection = Occtoo.Cards.SortDirection;

namespace Occtoo.Sdk.Tests.Destinations;

public class DestinationsClientTests
{
    private const string DestinationBody = """
        { "id": "webshop", "name": "Webshop feed", "description": null, "tags": ["Static", "eu"], "etag": 4,
          "createdAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-01T10:00:00Z" }
        """;

    private const string VersionBody = """
        { "version": 1, "id": "2b1d7f3a-5c2e-4b8f-9a6d-1e0c4f7a8b9c", "state": "New", "revision": 0, "authentication": "Protected",
          "allowedOrigins": ["*"], "languages": [ { "language": "en", "sourceLanguage": "en" }, { "language": "sv", "sourceLanguage": "sv-SE" } ],
          "defaultLanguage": "en", "description": null, "stopReason": [], "etag": 1,
          "createdAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-01T10:00:00Z" }
        """;

    private const string EndpointBody = """
        { "id": "products", "name": "Products", "description": null, "etag": 2,
          "content": { "cardId": "products", "properties": [ { "id": "title", "alias": "name", "facet": null }, { "id": "brand", "alias": null, "facet": { "size": 20, "header": { "en": "Brand" } } } ],
                       "segments": { "include": ["summer26"], "exclude": null },
                       "relations": [ { "id": "productVariants", "alias": "variants", "cardinality": "Plural",
                                        "content": { "cardId": "variants", "properties": [ { "id": "sku", "alias": null, "facet": null } ], "segments": null, "relations": null },
                                        "order": { "by": "rank", "direction": "Desc" } } ] },
          "pendingContent": null }
        """;

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions { Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")) });

    [Fact]
    public async Task Create_makes_the_destination_with_its_type_tag_and_the_first_version()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Created, DestinationBody).Respond(HttpStatusCode.Created, VersionBody);
        using var client = Client(handler);

        var created = (await client.Destinations.Create(
            CreateDestination.Static("webshop", "Webshop feed")
                .Tagged("eu")
                .WithLanguage(LanguageCode.From("en"))
                .WithLanguage(LanguageCode.From("sv"), LanguageCode.From("sv-SE")),
            TestContext.Current.CancellationToken)).Value;

        created.Destination.Type.GetValueOrThrow().ShouldBe(DestinationType.Static);
        created.Version.Id.Value.ShouldBe("2b1d7f3a-5c2e-4b8f-9a6d-1e0c4f7a8b9c");
        created.Version.Authentication.GetValueOrThrow().ShouldBe(DestinationAuthentication.Protected);
        created.Version.Languages[1].SourceLanguage.Value.ShouldBe("sv-SE");

        handler.Requests[0].Body.ShouldBe("""{"id":"webshop","name":"Webshop feed","tags":["Static","eu"]}""");
        handler.Requests[1].RequestUri!.AbsolutePath.ShouldBe("/v1/destinations/webshop/versions");
        handler.Requests[1].Body.ShouldBe(
            """{"authentication":"Protected","languages":[{"language":"en","sourceLanguage":"en"},{"language":"sv","sourceLanguage":"sv-SE"}],"defaultLanguage":"en","allowedOrigins":["*"]}""");
    }

    [Fact]
    public async Task A_public_destination_sends_allow_anonymous_and_a_create_without_languages_fails_first()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Created, DestinationBody).Respond(HttpStatusCode.Created, VersionBody);
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        (await client.Destinations.Create(CreateDestination.Dynamic("webshop", "Webshop feed"), ct)).Error.ShouldBeOfType<ValidationError>();
        handler.RequestCount.ShouldBe(0);

        (await client.Destinations.Create(CreateDestination.Dynamic("webshop", "Webshop feed").Public().WithLanguage(LanguageCode.From("en")), ct))
            .IsSuccess.ShouldBeTrue();
        JsonDocument.Parse(handler.Requests[1].Body!).RootElement.GetProperty("authentication").GetString().ShouldBe("AllowAnonymous");
    }

    [Fact]
    public async Task Replacing_tags_keeps_the_type_tag_and_sends_if_match()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, DestinationBody).Respond(HttpStatusCode.OK, DestinationBody);
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var destination = (await client.Destinations.Get("webshop", ct)).Value;
        (await client.Destinations.Update(destination.Edit().WithTags("us"), ct)).IsSuccess.ShouldBeTrue();

        handler.Requests[1].Header("If-Match").ShouldBe("\"4\"");
        handler.Requests[1].Body.ShouldBe("""{"tags":["Static","us"]}""");
    }

    [Fact]
    public async Task Endpoint_content_round_trips_properties_facets_segments_and_relations()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, EndpointBody).Respond(HttpStatusCode.Created, EndpointBody);
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var endpoint = (await client.Destinations.GetEndpoint("webshop", 1, "products", ct)).Value;
        var content = EndpointContent.Of("products")
            .Property("title", "name")
            .Facet("brand", 20, new Dictionary<LanguageCode, string> { [LanguageCode.From("en")] = "Brand" })
            .InSegments("summer26")
            .Relation("productVariants", "variants", EndpointCardinality.Plural,
                EndpointContent.Of("variants").Property("sku"), new EndpointOrder("rank", SortDirection.Descending));
        (await client.Destinations.CreateEndpoint("webshop", 1, CreateDestinationEndpoint.WithId("products", "Products", content), ct)).IsSuccess.ShouldBeTrue();

        endpoint.Content.Relations.ShouldHaveSingleItem().Order.GetValueOrThrow().Direction.ShouldBe(SortDirection.Descending);
        endpoint.Content.Properties[1].Facet.GetValueOrThrow().Size.ShouldBe(20);
        endpoint.Content.IncludeSegments.ShouldHaveSingleItem().Value.ShouldBe("summer26");
        JsonDocument.Parse(handler.Requests[1].Body!).RootElement.GetProperty("content").GetRawText().ShouldBe(
            """{"cardId":"products","properties":[{"id":"title","alias":"name"},{"id":"brand","facet":{"size":20,"header":{"en":"Brand"}}}],"segments":{"include":["summer26"]},"relations":[{"id":"productVariants","alias":"variants","cardinality":"Plural","content":{"cardId":"variants","properties":[{"id":"sku"}]},"order":{"by":"rank","direction":"Desc"}}]}""");
    }

    [Fact]
    public async Task Publishing_and_its_progress_use_the_version_routes()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Accepted, VersionBody.Replace("\"New\"", "\"Updating\""))
            .Respond(HttpStatusCode.OK, """
                { "status": "Running", "startedAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-01T10:01:00Z",
                  "steps": [ { "type": "Configuration", "title": "Saving the configuration", "status": "Completed", "attempt": 1, "timestamp": "2026-11-01T10:00:30Z" } ] }
                """)
            .Respond(HttpStatusCode.Accepted, "")
            .Respond(HttpStatusCode.NoContent, "");
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        (await client.Destinations.Publish("webshop", 1, ct)).Value.State.GetValueOrThrow().ShouldBe(DestinationVersionState.Updating);
        var publication = (await client.Destinations.GetLatestPublication("webshop", 1, ct)).Value;
        (await client.Destinations.DeleteVersion("webshop", 1, ETag.From(1), ct)).IsSuccess.ShouldBeTrue();
        (await client.Destinations.DiscardPendingContent("webshop", 1, "products", ct)).IsSuccess.ShouldBeTrue();

        publication.Status.GetValueOrThrow().ShouldBe(PublicationStatus.Running);
        publication.Steps.ShouldHaveSingleItem().Status.GetValueOrThrow().ShouldBe(PublicationStatus.Completed);
        handler.Requests.Select(request => $"{request.Method} {request.RequestUri!.AbsolutePath}").ShouldBe(
        [
            "POST /v1/destinations/webshop/versions/1/publications",
            "GET /v1/destinations/webshop/versions/1/publications/latest",
            "DELETE /v1/destinations/webshop/versions/1",
            "DELETE /v1/destinations/webshop/versions/1/endpoints/products/pending-content",
        ]);
        handler.Requests[2].Header("If-Match").ShouldBe("\"1\"");
    }

    [Fact]
    public void Names_are_three_to_fifty_characters()
    {
        DestinationName.TryFrom("ab").IsSuccess.ShouldBeFalse();
        EndpointName.TryFrom(new string('a', 51)).IsSuccess.ShouldBeFalse();
        DestinationName.TryFrom("Webshop").IsSuccess.ShouldBeTrue();
    }
}
