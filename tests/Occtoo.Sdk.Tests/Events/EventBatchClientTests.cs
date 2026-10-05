using System.Net;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Occtoo.Authentication;
using Occtoo.Events;
using Shouldly;
using Vogen;
using Xunit;

namespace Occtoo.Sdk.Tests.Events;

public class EventBatchClientTests
{
    private static readonly EventDestinationId Destination =
        EventDestinationId.From(Guid.Parse("5b0e2f4a-9c1d-4e8b-a7f3-6d2c1b0a9e8f"));

    private static readonly Guid LeaseId = Guid.Parse("0199a0c4-3c5e-7d8f-9a1b-2c3d4e5f6a7b");

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

    private static string Envelope(long sequence, string type = "source_entry.added") => $$"""
        {
          "specversion": "1.0",
          "id": "c0ffee00-0000-0000-0000-{{sequence:d12}}",
          "type": "{{type}}",
          "source": "/sources/products",
          "subject": "sku-{{sequence}}",
          "sequence": "003.{{sequence:d20}}",
          "data": { "sourceId": "products", "entryKey": "sku-{{sequence}}", "version": 1 }
        }
        """;

    private static string BatchBody(int generation, int attempt, params string[] events) => $$"""
        {
          "leaseId": "{{LeaseId}}",
          "generation": {{generation}},
          "attempt": {{attempt}},
          "events": [{{string.Join(",", events)}}]
        }
        """;

    private static EventBatchLease Lease(int generation = 0) => new(Destination, LeaseId, generation);

    // ── Pull ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pull_posts_to_the_durable_consumer_and_parses_the_leased_batch()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, BatchBody(generation: 0, attempt: 1, Envelope(1), Envelope(2)));
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var batch = result.Value.GetValueOrThrow();
        batch.Lease.ShouldBe(new EventBatchLease(Destination, LeaseId, 0));
        batch.Attempt.ShouldBe(1);
        batch.Events.Count.ShouldBe(2);
        batch.Events[0].ShouldBeOfType<SourceEntryAdded>().EntryKey.Value.ShouldBe("sku-1");
        batch.Events[1].Sequence.Value.ShouldBe("003.00000000000000000002");

        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Body.ShouldBeNull();
        request.RequestUri!.AbsoluteUri.ShouldBe(
            "https://api.occtoo.com/v1/event-destinations/5b0e2f4a-9c1d-4e8b-a7f3-6d2c1b0a9e8f/pull"
            + "?limit=20&visibility=60&workerId=worker-1");
    }

    [Fact]
    public async Task Pull_carries_the_limit_the_lease_duration_and_an_escaped_worker_id()
    {
        using var handler = new StubHandler().Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = Client(handler);

        await client.Events.Batch.Pull(
            Destination,
            "pod-7/loop 2",
            new EventBatchOptions { Limit = 100, LeaseDuration = TimeSpan.FromMinutes(5) },
            TestContext.Current.CancellationToken);

        handler.Requests.Single().RequestUri!.Query
            .ShouldBe("?limit=100&visibility=300&workerId=pod-7%2Floop%202");
    }

    [Fact]
    public async Task Pull_rounds_a_fractional_lease_duration_up_so_the_lease_is_never_shorter()
    {
        using var handler = new StubHandler().Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = Client(handler);

        await client.Events.Batch.Pull(
            Destination,
            "worker-1",
            new EventBatchOptions { LeaseDuration = TimeSpan.FromMilliseconds(1500) },
            TestContext.Current.CancellationToken);

        handler.Requests.Single().RequestUri!.Query.ShouldContain("visibility=2&");
    }

    [Fact]
    public async Task Pull_returns_nothing_when_the_durable_consumer_is_caught_up()
    {
        using var handler = new StubHandler().Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasNoValue.ShouldBeTrue();
    }

    [Fact]
    public async Task Pull_reads_a_redelivery_with_its_generation_and_attempt()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, BatchBody(generation: 3, attempt: 4, Envelope(9)));
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        var batch = result.Value.GetValueOrThrow();
        batch.Lease.Generation.ShouldBe(3);
        batch.Attempt.ShouldBe(4);
    }

    [Fact]
    public async Task Pull_accepts_numbers_sent_as_strings_as_the_contract_allows()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, $$"""
            { "leaseId": "{{LeaseId}}", "generation": "2", "attempt": "3", "events": [] }
            """);
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        var batch = result.Value.GetValueOrThrow();
        batch.Lease.Generation.ShouldBe(2);
        batch.Attempt.ShouldBe(3);
        batch.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pull_skips_a_malformed_event_and_keeps_the_rest_of_the_batch()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, BatchBody(0, 1, """{ "id": "no-type-here" }""", Envelope(2)));
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        result.Value.GetValueOrThrow().Events.ShouldHaveSingleItem()
            .Sequence.Value.ShouldBe("003.00000000000000000002");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "leaseId": "0199a0c4-3c5e-7d8f-9a1b-2c3d4e5f6a7b", "generation": 0, "attempt": 1 }""")]
    [InlineData("""{ "leaseId": "0199a0c4-3c5e-7d8f-9a1b-2c3d4e5f6a7b", "generation": 0, "attempt": 1, "events": null }""")]
    [InlineData("not json")]
    public async Task Pull_reports_a_batch_that_breaks_the_contract_as_unexpected(string body)
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, body);
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Pull_rejects_limits_outside_the_api_range_without_a_request(int limit)
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", new EventBatchOptions { Limit = limit }, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("Limit");
        handler.RequestCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(600_001)]
    public async Task Pull_rejects_lease_durations_outside_the_api_range_without_a_request(int milliseconds)
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination,
            "worker-1",
            new EventBatchOptions { LeaseDuration = TimeSpan.FromMilliseconds(milliseconds) },
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("LeaseDuration");
        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Pull_from_a_paused_durable_consumer_is_a_conflict_carrying_the_api_message()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Conflict, """
            { "message": "The event destination must be active to pull events." }
            """);
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ConflictError>().Message
            .ShouldContain("The event destination must be active to pull events.");
    }

    [Fact]
    public async Task Pull_from_a_destination_that_is_not_a_durable_consumer_is_a_validation_error()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.BadRequest, """
            { "message": "Pull and acknowledge are only supported for durable consumer destinations." }
            """);
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("only supported for durable consumer");
    }

    [Fact]
    public async Task Pull_from_an_unknown_destination_is_not_found()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.NotFound, """{ "message": "Not found." }""");
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NotFoundError>();
    }

    [Fact]
    public async Task Pull_with_a_full_in_flight_window_is_a_rate_limit_that_says_why()
    {
        using var handler = new StubHandler().Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""
                    { "message": "The in-flight window is full.", "committed": "003.1", "dispatched": "003.9" }
                    """),
            };
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(5));
            return response;
        });
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        var throttled = result.Error.ShouldBeOfType<RateLimitError>();
        throttled.Message.ShouldContain("window of unacknowledged events is full");
        throttled.RetryAfter.GetValueOrDefault().ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Pull_without_the_events_scope_is_forbidden()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Forbidden, "");
        using var client = Client(handler);

        var result = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ForbiddenError>();
    }

    // ── Acknowledge ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Acknowledge_ok_posts_the_lease_and_reads_the_committed_position()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            { "status": "committed", "committed": "003.00000000000000184467" }
            """);
        using var client = Client(handler);

        var result = await client.Events.Batch.Acknowledge(
            Lease(generation: 2), EventBatchOutcome.Ok, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.GetValueOrThrow().ShouldBe(EventBatchAcknowledgementStatus.Committed);
        result.Value.Committed.Value.ShouldBe("003.00000000000000184467");

        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.AbsoluteUri.ShouldBe(
            "https://api.occtoo.com/v1/event-destinations/5b0e2f4a-9c1d-4e8b-a7f3-6d2c1b0a9e8f/acknowledge");

        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("leaseId").GetGuid().ShouldBe(LeaseId);
        body.RootElement.GetProperty("generation").GetInt32().ShouldBe(2);
        body.RootElement.GetProperty("status").GetString().ShouldBe("ok");
    }

    [Fact]
    public async Task Acknowledge_failed_requeues_the_batch()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            { "status": "requeued", "committed": "003.00000000000000184001" }
            """);
        using var client = Client(handler);

        var result = await client.Events.Batch.Acknowledge(
            Lease(), EventBatchOutcome.Failed, TestContext.Current.CancellationToken);

        result.Value.Status.GetValueOrThrow().ShouldBe(EventBatchAcknowledgementStatus.Requeued);
        handler.Requests.Single().Body.ShouldContain("\"status\":\"failed\"");
    }

    [Fact]
    public async Task Acknowledge_of_an_expired_lease_succeeds_as_stale()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            { "status": "stale", "committed": "003.00000000000000184001" }
            """);
        using var client = Client(handler);

        var result = await client.Events.Batch.Acknowledge(
            Lease(), EventBatchOutcome.Ok, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.GetValueOrThrow().ShouldBe(EventBatchAcknowledgementStatus.Stale);
    }

    [Fact]
    public async Task Acknowledge_reads_a_status_newer_than_the_sdk_as_absent()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            { "status": "deferred", "committed": "003.00000000000000184001" }
            """);
        using var client = Client(handler);

        var result = await client.Events.Batch.Acknowledge(
            Lease(), EventBatchOutcome.Ok, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.HasNoValue.ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{ "status": "committed" }""")]
    [InlineData("""{ "status": "committed", "committed": "" }""")]
    public async Task Acknowledge_without_a_committed_position_is_unexpected(string body)
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, body);
        using var client = Client(handler);

        var result = await client.Events.Batch.Acknowledge(
            Lease(), EventBatchOutcome.Ok, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Fact]
    public async Task Acknowledge_rejects_a_missing_lease_without_a_request()
    {
        using var handler = new StubHandler();
        using var client = Client(handler);

        var result = await client.Events.Batch.Acknowledge(
            null!, EventBatchOutcome.Ok, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>();
        handler.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Acknowledge_on_a_deleted_destination_is_not_found()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.NotFound, """{ "message": "Not found." }""");
        using var client = Client(handler);

        var result = await client.Events.Batch.Acknowledge(
            Lease(), EventBatchOutcome.Ok, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NotFoundError>();
    }

    [Fact]
    public async Task A_pulled_batch_acknowledges_against_the_destination_it_was_leased_from()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, BatchBody(generation: 5, attempt: 2, Envelope(1)))
            .Respond(HttpStatusCode.OK, """{ "status": "committed", "committed": "003.00000000000000000001" }""");
        using var client = Client(handler);

        var pulled = await client.Events.Batch.Pull(
            Destination, "worker-1", cancellationToken: TestContext.Current.CancellationToken);
        var batch = pulled.Value.GetValueOrThrow();

        await client.Events.Batch.Acknowledge(batch.Lease, EventBatchOutcome.Ok, TestContext.Current.CancellationToken);

        var acknowledgement = handler.Requests[1];
        acknowledgement.RequestUri!.AbsolutePath
            .ShouldBe("/v1/event-destinations/5b0e2f4a-9c1d-4e8b-a7f3-6d2c1b0a9e8f/acknowledge");
        acknowledgement.Body.ShouldContain("\"generation\":5");
    }

    // ── Identifiers ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_worker_id_must_not_be_blank(string value) =>
        Should.Throw<ValueObjectValidationException>(() => EventWorkerId.From(value));

    [Fact]
    public void A_worker_id_is_at_most_200_characters()
    {
        EventWorkerId.From(new string('w', 200)).Value.Length.ShouldBe(200);
        Should.Throw<ValueObjectValidationException>(() => EventWorkerId.From(new string('w', 201)));
    }

    [Fact]
    public void An_event_destination_id_must_not_be_empty() =>
        Should.Throw<ValueObjectValidationException>(() => EventDestinationId.From(Guid.Empty));
}
