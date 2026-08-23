using System.Net;
using System.Text.Json;
using Occtoo.Authentication;
using Occtoo.Sources;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Sources;

public class SourcesClientTests
{
    private const string AcceptedBody = """
        {
          "correlationId": "83b538a7-df7c-4cf4-b988-cd3b71c4cd90",
          "sourceId": "products",
          "acceptedAt": "2026-08-13T10:15:30Z",
          "acceptedEntryCount": 1,
          "newPropertiesFound": [
            { "id": "tags", "type": "List", "delimiter": "," }
          ]
        }
        """;

    private static readonly SourceId Products = SourceId.From("products");

    private static OcctooClient Client(StubHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new OcctooClient(httpClient, new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });
    }

    private static SourceEntry ChairEntry() =>
        SourceEntry.WithId("sku-123")
            .WithLocalizedText("name", "Blue chair", "en")
            .WithDecimal("price", 100.111m)
            .WithTimestamp("publishedAt", DateTimeOffset.Parse("2026-01-01T00:00:00Z", null))
            .WithBoolean("inStock", true)
            .WithList("tags", "summer", "sale");

    [Fact]
    public async Task Posts_typed_json_to_the_source_and_returns_the_receipt()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Accepted, AcceptedBody);
        using var client = Client(handler);

        var result = await client.Sources.IngestEntries(
            Products,
            [ChairEntry()],
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var receipt = result.Value;
        receipt.CorrelationId.Value.ShouldBe(Guid.Parse("83b538a7-df7c-4cf4-b988-cd3b71c4cd90"));
        receipt.SourceId.ShouldBe(Products);
        receipt.AcceptedEntryCount.ShouldBe(1);
        receipt.NewProperties.ShouldHaveSingleItem();
        receipt.NewProperties[0].Id.Value.ShouldBe("tags");
        receipt.NewProperties[0].Type.ShouldBe(SourcePropertyType.List);
        receipt.NewProperties[0].Delimiter.GetValueOrDefault().ShouldBe(",");

        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://api.occtoo.com/sources/products");
    }

    [Fact]
    public async Task Serializes_values_as_their_native_json_types()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Accepted, AcceptedBody);
        using var client = Client(handler);

        await client.Sources.IngestEntries(Products, [ChairEntry()], TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
        var entry = body.RootElement.GetProperty("entries")[0];
        entry.GetProperty("id").GetString().ShouldBe("sku-123");

        var properties = entry.GetProperty("properties").EnumerateArray().ToArray();

        var name = properties.Single(p => p.GetProperty("id").GetString() == "name");
        name.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.String);
        name.GetProperty("value").GetString().ShouldBe("Blue chair");
        name.GetProperty("language").GetString().ShouldBe("en");

        var price = properties.Single(p => p.GetProperty("id").GetString() == "price");
        price.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Number);
        price.GetProperty("value").GetDecimal().ShouldBe(100.111m);
        price.TryGetProperty("language", out _).ShouldBeFalse();

        var published = properties.Single(p => p.GetProperty("id").GetString() == "publishedat");
        published.GetProperty("value").GetDateTimeOffset()
            .ShouldBe(DateTimeOffset.Parse("2026-01-01T00:00:00Z", null));

        var inStock = properties.Single(p => p.GetProperty("id").GetString() == "instock");
        inStock.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.True);

        var tags = properties.Single(p => p.GetProperty("id").GetString() == "tags");
        tags.GetProperty("value").EnumerateArray().Select(item => item.GetString())
            .ShouldBe(["summer", "sale"]);
    }

    [Fact]
    public async Task A_cleared_property_is_sent_as_json_null()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Accepted, AcceptedBody);
        using var client = Client(handler);

        await client.Sources.IngestEntries(
            Products,
            [SourceEntry.WithId("sku-1").WithCleared("discontinuedReason")],
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body!);
        var property = body.RootElement.GetProperty("entries")[0].GetProperty("properties")[0];
        property.GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Rejects_an_empty_batch_without_a_round_trip()
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        var result = await client.Sources.IngestEntries(Products, [], TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>();
        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Even_a_null_batch_is_a_result_not_an_exception()
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        // The compiler already warns here; at runtime the mistake stays on the
        // failure track like every other rejected input.
        var result = await client.Sources.IngestEntries(Products, null!, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>();
        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Maps_a_validation_rejection_with_its_per_path_messages()
    {
        const string problem = """
            {
              "title": "One or more validation errors occurred.",
              "status": 400,
              "traceId": "00-abc-def-01",
              "errors": {
                "entries[0].properties[1].value": [ "Expected a number for property 'price'." ]
              }
            }
            """;

        using var handler = new StubHandler().Respond(HttpStatusCode.BadRequest, problem);
        using var client = Client(handler);

        var result = await client.Sources.IngestEntries(
            Products,
            [ChairEntry()],
            TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Message.ShouldContain("validation");
        error.Message.ShouldContain("00-abc-def-01");
        error.Failures.ShouldContainKey("entries[0].properties[1].value");
        error.Failures["entries[0].properties[1].value"].Single().ShouldContain("price");
    }

    [Fact]
    public async Task Maps_a_throttled_request_to_a_retryable_rate_limit_error()
    {
        using var handler = new StubHandler().Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(17));
            return response;
        });
        using var client = Client(handler);

        var result = await client.Sources.IngestEntries(
            Products,
            [ChairEntry()],
            TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<RateLimitError>();
        error.ShouldBeAssignableTo<TransientError>();
        error.RetryAfter.GetValueOrDefault().ShouldBe(TimeSpan.FromSeconds(17));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(AuthenticationError))]
    [InlineData(HttpStatusCode.Forbidden, typeof(ForbiddenError))]
    [InlineData(HttpStatusCode.NotFound, typeof(NotFoundError))]
    [InlineData(HttpStatusCode.Conflict, typeof(ConflictError))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(ServerError))]
    [InlineData(HttpStatusCode.ServiceUnavailable, typeof(ServerError))]
    public async Task Maps_each_documented_status_to_its_error_type(HttpStatusCode status, Type expected)
    {
        using var handler = new StubHandler().Respond(status, """{"title":"nope"}""");
        using var client = Client(handler);

        var result = await client.Sources.IngestEntries(
            Products,
            [ChairEntry()],
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType(expected);
    }

    [Fact]
    public async Task A_202_with_a_non_json_body_is_a_result_not_an_exception()
    {
        using var handler = new StubHandler().Respond(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("upstream says hi", System.Text.Encoding.UTF8, "text/plain"),
        });
        using var client = Client(handler);

        var result = await client.Sources.IngestEntries(
            Products, [ChairEntry()], TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Fact]
    public async Task An_exception_from_a_user_layered_handler_is_a_result_not_an_exception()
    {
        // A consumer-added resilience handler may throw its own types (Polly
        // timeout, circuit breaker); the no-throw contract must hold anyway.
        using var handler = new StubHandler().Respond(_ => throw new InvalidOperationException("circuit open"));
        using var client = Client(handler);

        var result = await client.Sources.IngestEntries(
            Products, [ChairEntry()], TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>().Message.ShouldContain("circuit open");
    }

    [Fact]
    public async Task Reports_an_unreachable_api_as_a_transient_network_error()
    {
        using var handler = new StubHandler().Respond(_ => throw new HttpRequestException("down"));
        using var client = Client(handler);

        var result = await client.Sources.IngestEntries(
            Products,
            [ChairEntry()],
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NetworkError>().ShouldBeAssignableTo<TransientError>();
    }

    [Fact]
    public async Task Surfaces_a_failed_credential_as_a_result_not_an_exception()
    {
        using var transport = new StubHandler()
            .Respond(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""");
        using var tokenClient = new HttpClient(transport);
        using var credential = new ClientCredentialsCredential(
            new OcctooAuthorityOptions
            {
                ClientId = ClientId.From("client-abc"),
                Audience = Audience.From("tenant-1"),
            },
            ClientSecret.From("wrong"),
            tokenClient,
            new ZiggyCreatures.Caching.Fusion.FusionCache(new ZiggyCreatures.Caching.Fusion.FusionCacheOptions()));

        using var apiTransport = new StubHandler();
        using var httpClient = new HttpClient(
            new Occtoo.Http.OcctooAuthenticationHandler(credential) { InnerHandler = apiTransport });
        using var client = new OcctooClient(httpClient, new OcctooClientOptions
        {
            Credential = credential,
        });

        var result = await client.Sources.IngestEntries(
            Products,
            [ChairEntry()],
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<AuthenticationError>();
        apiTransport.RequestCount.ShouldBe(0);
    }
}
