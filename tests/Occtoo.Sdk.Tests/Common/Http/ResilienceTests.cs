using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Occtoo.Authentication;
using Occtoo.DependencyInjection;
using Occtoo.Sources;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Common.Http;

public class ResilienceTests
{
    private const string AcceptedBody = """
        {
          "correlationId": "83b538a7-df7c-4cf4-b988-cd3b71c4cd90",
          "sourceId": "products",
          "acceptedAt": "2026-08-13T10:15:30Z",
          "acceptedEntryCount": 1,
          "newPropertiesFound": []
        }
        """;

    private static readonly OcctooResilienceOptions FastRetries = new()
    {
        MaxRetryAttempts = 3,
        BaseDelay = TimeSpan.FromMilliseconds(1),
        UseJitter = false,
    };

    private static OcctooClient Client(StubHandler handler, OcctooResilienceOptions resilience)
    {
        var options = new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
            Resilience = resilience,
        };

        // The DI path builds the documented pipeline (auth outermost, retries
        // inside); using it here means these tests cover that wiring too.
        var services = new ServiceCollection();
        services.AddOcctooClient(_ => options);
        services
            .AddHttpClient(OcctooServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider().GetRequiredService<OcctooClient>();
    }

    private static SourceEntry Entry() => SourceEntry.WithId("sku-1").WithText("name", "chair").Build();

    [Fact]
    public async Task A_throttled_request_waits_out_retry_after_and_succeeds()
    {
        using var handler = new StubHandler()
            .Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new(TimeSpan.Zero);
                return response;
            })
            .Respond(HttpStatusCode.Accepted, AcceptedBody);

        using var client = Client(handler, FastRetries);

        var result = await client.Sources.IngestEntries(
            SourceId.From("products"), [Entry()], TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_server_error_is_retried_with_backoff_until_it_passes()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.ServiceUnavailable, "{}")
            .Respond(HttpStatusCode.ServiceUnavailable, "{}")
            .Respond(HttpStatusCode.Accepted, AcceptedBody);

        using var client = Client(handler, FastRetries);

        var result = await client.Sources.IngestEntries(
            SourceId.From("products"), [Entry()], TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.RequestCount.ShouldBe(3);
    }

    [Fact]
    public async Task Exhausted_retries_surface_the_usual_typed_error()
    {
        using var handler = new StubHandler().RespondAlways(HttpStatusCode.TooManyRequests, "{}");
        using var client = Client(handler, FastRetries);

        var result = await client.Sources.IngestEntries(
            SourceId.From("products"), [Entry()], TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<RateLimitError>();
        // The initial attempt plus every configured retry.
        handler.RequestCount.ShouldBe(4);
    }

    [Fact]
    public async Task A_non_transient_rejection_is_not_retried()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.BadRequest, """{"title":"bad"}""");
        using var client = Client(handler, FastRetries);

        var result = await client.Sources.IngestEntries(
            SourceId.From("products"), [Entry()], TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>();
        handler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task Disabled_resilience_means_a_single_attempt()
    {
        using var handler = new StubHandler().RespondAlways(HttpStatusCode.ServiceUnavailable, "{}");
        using var client = Client(handler, new OcctooResilienceOptions { Enabled = false });

        var result = await client.Sources.IngestEntries(
            SourceId.From("products"), [Entry()], TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ServerError>();
        handler.RequestCount.ShouldBe(1);
    }

    [Fact]
    public void Nonsense_retry_settings_fail_at_construction()
    {
        var options = new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
            Resilience = new OcctooResilienceOptions { MaxRetryAttempts = -1 },
        };

        Should.Throw<InvalidOperationException>(() => new OcctooClient(options));
    }
}
