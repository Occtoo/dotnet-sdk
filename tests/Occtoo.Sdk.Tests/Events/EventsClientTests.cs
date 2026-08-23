using System.Net;
using System.Text;
using CSharpFunctionalExtensions;
using Occtoo.Authentication;
using Occtoo.Events;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Events;

public class EventsClientTests
{
    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

    private static string Envelope(long sequence, string type = "source.created") => $$"""
        {
          "id": "c0ffee00-0000-0000-0000-{{sequence:d12}}",
          "type": "{{type}}",
          "sequence": "{{sequence:d16}}",
          "data": { "sourceId": "products" }
        }
        """;

    private static string PageBody(bool hasMore, string? after, params long[] sequences) => $$"""
        {
          "items": [{{string.Join(",", sequences.Select(s => Envelope(s)))}}],
          "after": {{(after is null ? "null" : $"\"{after}\"")}},
          "hasMore": {{(hasMore ? "true" : "false")}}
        }
        """;

    // ── Pull ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pull_requests_the_events_endpoint_and_parses_the_page()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "id": "9f4f2c74-6a3e-4a5b-9a2f-3e6f0d1c2b3a",
                  "type": "source.created",
                  "sequence": "0000000000000001",
                  "data": { "sourceId": "products" }
                }
              ],
              "after": "0000000000000001",
              "hasMore": true,
              "total": 41
            }
            """);
        using var client = Client(handler);

        var result = await client.Events.Pull(cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var page = result.Value;
        page.Items.ShouldHaveSingleItem().ShouldBeOfType<SourceCreated>().SourceId.Value.ShouldBe("products");
        page.Next.GetValueOrThrow().Value.ShouldBe("0000000000000001");
        page.HasMore.ShouldBeTrue();
        page.Total.GetValueOrDefault().ShouldBe(41);

        handler.Requests.Single().RequestUri!.ToString()
            .ShouldBe("https://api.occtoo.com/v1/events?limit=100");
    }

    [Fact]
    public async Task Pull_carries_cursor_filter_and_total_in_the_query()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, PageBody(false, null));
        using var client = Client(handler);

        await client.Events.Pull(
            new EventQuery
            {
                Limit = 5,
                IncludeTotal = true,
                After = Maybe.From(EventCursor.From("0000000000000009")),
                Filter = Maybe.From(EventFilter.OfType<SourceEntryAdded>(e => e.WithSource("products"))),
            },
            TestContext.Current.CancellationToken);

        // AbsoluteUri keeps the escaping Uri.ToString() would cosmetically undo.
        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe(
            "https://api.occtoo.com/v1/events?limit=5&total=true&after=0000000000000009"
            + "&filter=type%20eq%20%22source_entry.added%22%20and%20sourceId%20eq%20%22products%22");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Pull_rejects_limits_outside_the_api_range(int limit)
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        var result = await client.Events.Pull(
            new EventQuery { Limit = limit },
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ValidationError>();
        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Pull_classifies_api_rejections()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Forbidden, """{ "title": "missing scope" }""");
        using var client = Client(handler);

        var result = await client.Events.Pull(cancellationToken: TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ForbiddenError>();
    }

    [Fact]
    public async Task Pull_skips_a_malformed_item_and_keeps_the_rest()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, $$"""
            {
              "items": [{ "id": "no-type-here" }, {{Envelope(2)}}],
              "hasMore": false
            }
            """);
        using var client = Client(handler);

        var result = await client.Events.Pull(cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldHaveSingleItem().Sequence.Value.ShouldBe("0000000000000002");
    }

    // ── GetMetadata ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetadata_reads_the_stream_shape()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            {
              "first": { "sequence": "003.00000000000000184001", "time": "2026-07-04T08:10:00Z" },
              "latest": { "sequence": "003.00000000000000184467", "time": "2026-07-04T09:15:12.345Z" },
              "after": "opaque-tail-cursor",
              "total": 184
            }
            """);
        using var client = Client(handler);

        var result = await client.Events.GetMetadata(cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var metadata = result.Value;
        metadata.First.GetValueOrThrow().Sequence.Value.ShouldBe("003.00000000000000184001");
        metadata.First.GetValueOrThrow().Time.GetValueOrDefault()
            .ShouldBe(DateTimeOffset.Parse("2026-07-04T08:10:00Z", null));
        metadata.Latest.GetValueOrThrow().Sequence.Value.ShouldBe("003.00000000000000184467");
        metadata.After.GetValueOrThrow().Value.ShouldBe("opaque-tail-cursor");
        metadata.Total.ShouldBe(184);

        handler.Requests.Single().RequestUri!.ToString()
            .ShouldBe("https://api.occtoo.com/v1/events/metadata");
    }

    [Fact]
    public async Task GetMetadata_carries_the_filter_and_maps_an_empty_view()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            { "first": null, "latest": null, "after": null, "total": 0 }
            """);
        using var client = Client(handler);

        var result = await client.Events.GetMetadata(
            EventFilter.OfType<SourceEntryAdded>(e => e.WithSource("products")),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var metadata = result.Value;
        metadata.First.HasNoValue.ShouldBeTrue();
        metadata.Latest.HasNoValue.ShouldBeTrue();
        metadata.After.HasNoValue.ShouldBeTrue();
        metadata.Total.ShouldBe(0);

        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe(
            "https://api.occtoo.com/v1/events/metadata"
            + "?filter=type%20eq%20%22source_entry.added%22%20and%20sourceId%20eq%20%22products%22");
    }

    [Fact]
    public async Task GetMetadata_classifies_api_rejections()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Forbidden, """{ "title": "missing scope" }""");
        using var client = Client(handler);

        var result = await client.Events.GetMetadata(cancellationToken: TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ForbiddenError>();
    }

    // ── PullAll ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PullAll_follows_cursors_until_the_stream_is_exhausted()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, PageBody(hasMore: true, after: "0000000000000002", 1, 2))
            .Respond(HttpStatusCode.OK, PageBody(hasMore: false, after: "0000000000000003", 3));
        using var client = Client(handler);

        var sequences = new List<string>();
        await foreach (var evt in client.Events.PullAll(cancellationToken: TestContext.Current.CancellationToken))
            sequences.Add(evt.Sequence.Value);

        sequences.ShouldBe(["0000000000000001", "0000000000000002", "0000000000000003"]);
        handler.Requests[1].RequestUri!.Query.ShouldContain("after=0000000000000002");
    }

    [Fact]
    public async Task PullAll_throws_the_typed_exception_when_a_page_fails()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Unauthorized, """{ "title": "expired" }""");
        using var client = Client(handler);

        var exception = await Should.ThrowAsync<OcctooEventsException>(async () =>
        {
            await foreach (var _ in client.Events.PullAll(cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        exception.Error.ShouldBeOfType<AuthenticationError>();
    }

    // ── Stream ──────────────────────────────────────────────────────────────

    private static readonly EventStreamOptions FastReconnect = new()
    {
        InitialReconnectDelay = TimeSpan.FromMilliseconds(1),
        MaxReconnectDelay = TimeSpan.FromMilliseconds(2),
    };

    private static HttpResponseMessage SseResponse(params string[] frames) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Concat(frames), Encoding.UTF8, "text/event-stream"),
        };

    private static string Frame(long sequence) =>
        $"id: {sequence:d16}\ndata: {Envelope(sequence).ReplaceLineEndings(" ")}\n\n";

    [Fact]
    public async Task Stream_yields_events_and_resumes_after_the_last_delivered_one()
    {
        using var handler = new StubHandler()
            .Respond(_ => SseResponse(": heartbeat\n\n", Frame(1), Frame(2)))
            .Respond(_ => SseResponse(Frame(3)));
        using var client = Client(handler);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var sequences = new List<string>();
        var enumeration = async () =>
        {
            await foreach (var evt in client.Events.Stream(FastReconnect, cts.Token))
            {
                sequences.Add(evt.Sequence.Value);
                if (sequences.Count == 3)
                    cts.Cancel();
            }
        };

        await Should.ThrowAsync<OperationCanceledException>(enumeration);

        sequences.ShouldBe(["0000000000000001", "0000000000000002", "0000000000000003"]);
        handler.Requests[0].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/events/stream");
        handler.Requests[0].Header("Accept").ShouldBe("text/event-stream");
        handler.Requests[0].Header("Last-Event-ID").ShouldBeNull();
        // The reconnect resumes strictly after the last delivered event.
        handler.Requests[1].Header("Last-Event-ID").ShouldBe("0000000000000002");
    }

    [Fact]
    public async Task Stream_subscribes_from_the_given_cursor_and_filter()
    {
        using var handler = new StubHandler().Respond(_ => SseResponse(Frame(10)));
        using var client = Client(handler);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var options = FastReconnect with
        {
            After = Maybe.From(EventCursor.From("0000000000000009")),
            Filter = Maybe.From(EventFilter.OfType<SourceEvent>()),
        };

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.Events.Stream(options, cts.Token))
                cts.Cancel();
        });

        var request = handler.Requests[0];
        request.RequestUri!.ToString().ShouldStartWith(
            "https://api.occtoo.com/v1/events/stream?after=0000000000000009&filter=");
        request.Header("Last-Event-ID").ShouldBe("0000000000000009");
    }

    [Fact]
    public async Task Stream_reconnects_through_transient_connect_failures()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.TooManyRequests, """{ "title": "slow down" }""")
            .Respond(_ => SseResponse(Frame(1)));
        using var client = Client(handler);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var sequences = new List<string>();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var evt in client.Events.Stream(FastReconnect, cts.Token))
            {
                sequences.Add(evt.Sequence.Value);
                cts.Cancel();
            }
        });

        sequences.ShouldBe(["0000000000000001"]);
        handler.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task Stream_throws_the_typed_exception_for_permanent_failures()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Unauthorized, """{ "title": "revoked" }""");
        using var client = Client(handler);

        var exception = await Should.ThrowAsync<OcctooEventsException>(async () =>
        {
            await foreach (var _ in client.Events.Stream(
                FastReconnect, TestContext.Current.CancellationToken))
            {
            }
        });

        exception.Error.ShouldBeOfType<AuthenticationError>();
    }

    [Fact]
    public async Task Stream_skips_unparseable_frames_and_keeps_going()
    {
        using var handler = new StubHandler()
            .Respond(_ => SseResponse("data: not json\n\n", Frame(1)));
        using var client = Client(handler);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var sequences = new List<string>();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var evt in client.Events.Stream(FastReconnect, cts.Token))
            {
                sequences.Add(evt.Sequence.Value);
                cts.Cancel();
            }
        });

        sequences.ShouldBe(["0000000000000001"]);
    }

    [Fact]
    public async Task Stream_rejects_nonsensical_reconnect_delays()
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in client.Events.Stream(
                new EventStreamOptions { InitialReconnectDelay = TimeSpan.Zero },
                TestContext.Current.CancellationToken))
            {
            }
        });
    }
}
