using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Occtoo.Authentication;
using Occtoo.Sources;
using Occtoo.Telemetry;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace Occtoo.Sdk.Tests.Common.Telemetry;

public class OcctooTelemetryTests
{
    private const string AcceptedBody = """
        {
          "correlationId": "83b538a7-df7c-4cf4-b988-cd3b71c4cd90",
          "sourceId": "otel-products",
          "acceptedAt": "2026-08-13T10:15:30Z",
          "acceptedEntryCount": 2,
          "newPropertiesFound": []
        }
        """;

    /// <summary>
    /// Subscribes to the SDK's source for the duration of a test. Activities
    /// from concurrently running tests share the source, so assertions filter
    /// by a per-test source id rather than counting.
    /// </summary>
    private static (ActivityListener Listener, ConcurrentQueue<Activity> Stopped) Listen()
    {
        var stopped = new ConcurrentQueue<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OcctooTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        return (listener, stopped);
    }

    [Fact]
    public async Task The_documented_add_source_wiring_delivers_spans_to_an_exporter()
    {
        var exported = new List<Activity>();
        using var provider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(OcctooTelemetry.ActivitySourceName)
            .AddInMemoryExporter(exported)
            .Build();

        using var handler = new StubHandler().Respond(HttpStatusCode.Accepted, AcceptedBody);
        using var httpClient = new HttpClient(handler);
        using var client = new OcctooClient(httpClient, new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

        (await client.Sources.IngestEntries(
            SourceId.From("otel-provider"),
            [SourceEntry.WithId("sku-1").WithText("name", "chair").Build()],
            TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        provider.ForceFlush();
        exported.ShouldContain(activity => activity.DisplayName == "ingest otel-provider");
    }

    [Fact]
    public async Task An_accepted_ingest_emits_a_client_span_with_the_semconv_shape()
    {
        var (listener, stopped) = Listen();
        using (listener)
        {
            using var handler = new StubHandler().Respond(HttpStatusCode.Accepted, AcceptedBody);
            using var httpClient = new HttpClient(handler);
            using var client = new OcctooClient(httpClient, new OcctooClientOptions
            {
                Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
            });

            (await client.Sources.IngestEntries(
                SourceId.From("otel-products"),
                [
                    SourceEntry.WithId("sku-1").WithText("name", "chair").Build(),
                    SourceEntry.WithId("sku-2").WithText("name", "table").Build(),
                ],
                TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

            var span = stopped.Single(activity =>
                (string?)activity.GetTagItem("occtoo.source.id") == "otel-products");

            span.DisplayName.ShouldBe("ingest otel-products");
            span.Kind.ShouldBe(ActivityKind.Client);
            span.Status.ShouldBe(ActivityStatusCode.Unset);
            span.GetTagItem("occtoo.ingest.entry_count").ShouldBe(2);
            span.GetTagItem("occtoo.ingest.correlation_id")
                .ShouldBe(Guid.Parse("83b538a7-df7c-4cf4-b988-cd3b71c4cd90"));
        }
    }

    [Fact]
    public async Task A_rejected_ingest_marks_the_span_failed_with_the_error_type()
    {
        var (listener, stopped) = Listen();
        using (listener)
        {
            using var handler = new StubHandler().Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(5));
                return response;
            });
            using var httpClient = new HttpClient(handler);
            using var client = new OcctooClient(httpClient, new OcctooClientOptions
            {
                Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
            });

            (await client.Sources.IngestEntries(
                SourceId.From("otel-throttled"),
                [SourceEntry.WithId("sku-1").WithText("name", "chair").Build()],
                TestContext.Current.CancellationToken)).IsFailure.ShouldBeTrue();

            var span = stopped.Single(activity =>
                (string?)activity.GetTagItem("occtoo.source.id") == "otel-throttled");

            span.Status.ShouldBe(ActivityStatusCode.Error);
            span.GetTagItem("error.type").ShouldBe("RateLimitError");
        }
    }

    [Fact]
    public async Task Token_acquisition_emits_an_authenticate_span_nested_under_the_operation()
    {
        var (listener, stopped) = Listen();
        using (listener)
        {
            using var transport = new StubHandler()
                .Respond(HttpStatusCode.OK, """{"access_token":"token-1","expires_in":3600}""")
                .Respond(HttpStatusCode.Accepted, AcceptedBody);

            using var tokenClient = new HttpClient(transport);
            using var credential = new ClientCredentialsCredential(
                new OcctooAuthorityOptions
                {
                    ClientId = ClientId.From("otel-client"),
                    Audience = Audience.From("tenant-1"),
                },
                ClientSecret.From("secret"),
                tokenClient,
                new FusionCache(new FusionCacheOptions()));

            using var httpClient = new HttpClient(
                new Occtoo.Http.OcctooAuthenticationHandler(credential) { InnerHandler = transport });
            using var client = new OcctooClient(httpClient, new OcctooClientOptions
            {
                Credential = credential,
            });

            (await client.Sources.IngestEntries(
                SourceId.From("otel-nested"),
                [SourceEntry.WithId("sku-1").WithText("name", "chair").Build()],
                TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

            var ingest = stopped.Single(activity =>
                (string?)activity.GetTagItem("occtoo.source.id") == "otel-nested");
            var authenticate = stopped.Single(activity =>
                activity.DisplayName == "authenticate"
                && (string?)activity.GetTagItem("occtoo.credential.type") == "client_credentials"
                && activity.ParentSpanId == ingest.SpanId);

            authenticate.Kind.ShouldBe(ActivityKind.Client);
            authenticate.Status.ShouldBe(ActivityStatusCode.Unset);
        }
    }
}
