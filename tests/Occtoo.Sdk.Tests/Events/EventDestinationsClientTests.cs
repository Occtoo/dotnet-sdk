using System.Net;
using System.Text.Json;
using Occtoo.Authentication;
using Occtoo.Events;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Events;

public class EventDestinationsClientTests
{
    private const string Id = "6f2c25c1-0000-4000-8000-000000000001";

    private const string WebhookBody = $$"""
        {
          "id": "{{Id}}", "name": "Orders hook", "type": "Webhook", "status": "PendingVerification",
          "filter": "type == 'source_entry.added'", "target": "https://hooks.example.com/occtoo",
          "startsAfterSequence": null, "createdAt": "2026-11-01T10:00:00Z", "updatedAt": "2026-11-01T10:00:00Z"
        }
        """;

    private static readonly EventDestinationId Destination = EventDestinationId.From(Guid.Parse(Id));

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions { Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")) });

    [Fact]
    public async Task Create_sends_the_type_filter_and_configuration_and_returns_the_secret_once()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Created, $$"""
            { "eventDestination": {{WebhookBody}}, "signingSecret": "whsec_c2VjcmV0" }
            """);
        using var client = Client(handler);

        var created = (await client.Events.Destinations.Create(
            CreateEventDestination.Webhook("Orders hook", new Uri("https://hooks.example.com/occtoo"), EventFilter.OfType<SourceEntryAdded>()),
            TestContext.Current.CancellationToken)).Value;

        created.SigningSecret.GetValueOrThrow().Value.ShouldBe("whsec_c2VjcmV0");
        $"{created.SigningSecret.GetValueOrThrow()}".ShouldBe("***");
        created.SigningSecret.GetValueOrThrow().ToString(System.Globalization.CultureInfo.InvariantCulture).ShouldBe("***");
        created.Destination.Status.GetValueOrThrow().ShouldBe(EventDestinationStatus.PendingVerification);
        created.Destination.StartsAfter.HasNoValue.ShouldBeTrue();

        var request = handler.Requests.Single();
        request.RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/event-destinations");
        var body = JsonDocument.Parse(request.Body!).RootElement;
        body.GetProperty("type").GetString().ShouldBe("Webhook");
        body.GetProperty("filter").GetString().ShouldBe(EventFilter.OfType<SourceEntryAdded>().ToString());
        body.GetProperty("configuration").GetProperty("endpoint").GetString().ShouldBe("https://hooks.example.com/occtoo");
    }

    [Fact]
    public async Task A_durable_consumer_is_created_without_configuration()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Created, $$"""
            { "eventDestination": {{WebhookBody.Replace("\"Webhook\"", "\"DurableConsumer\"")}}, "signingSecret": null }
            """);
        using var client = Client(handler);

        var created = (await client.Events.Destinations.Create(
            CreateEventDestination.DurableConsumer("Indexer", EventFilter.OfType<SourceEntryEvent>()),
            TestContext.Current.CancellationToken)).Value;

        created.SigningSecret.HasNoValue.ShouldBeTrue();
        JsonDocument.Parse(handler.Requests.Single().Body!).RootElement.TryGetProperty("configuration", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Update_sends_only_the_changed_fields_and_never_prints_a_connection_string()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, WebhookBody);
        using var client = Client(handler);
        var connection = AzureConnectionString.From("DefaultEndpointsProtocol=https;AccountKey=secret");

        var changes = new EventDestination(Destination, "Orders hook", EventDestinationType.AzureStorageQueue, EventDestinationStatus.Active,
                EventFilter.Raw("type == 'card.updated'"), "orders", CSharpFunctionalExtensions.Maybe<EventSequence>.None,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            .Edit()
            .WithName("Orders queue")
            .WithAzureStorageQueue(connection, "orders");
        (await client.Events.Destinations.Update(changes, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        changes.ToString().ShouldNotContain("secret");
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Patch);
        request.RequestUri!.AbsoluteUri.ShouldBe($"https://api.occtoo.com/v1/event-destinations/{Id}");
        request.Body.ShouldBe("""{"name":"Orders queue","configuration":{"connectionString":"DefaultEndpointsProtocol=https;AccountKey=secret","queueName":"orders"}}""");
    }

    [Theory]
    [InlineData("verify")]
    [InlineData("pause")]
    [InlineData("resume")]
    public async Task Lifecycle_actions_post_to_their_route(string action)
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, WebhookBody);
        using var client = Client(handler);
        var destinations = client.Events.Destinations;
        var ct = TestContext.Current.CancellationToken;

        var result = action switch
        {
            "verify" => await destinations.Verify(Destination, ct),
            "pause" => await destinations.Pause(Destination, ct),
            _ => await destinations.Resume(Destination, ct),
        };

        result.IsSuccess.ShouldBeTrue();
        handler.Requests.Single().Method.ShouldBe(HttpMethod.Post);
        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe($"https://api.occtoo.com/v1/event-destinations/{Id}/{action}");
    }

    [Fact]
    public async Task A_pause_of_a_destination_that_is_not_active_is_a_conflict()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.Conflict, """{ "message": "not active" }""");
        using var client = Client(handler);

        var result = await client.Events.Destinations.Pause(Destination, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ConflictError>();
    }

    [Fact]
    public async Task List_and_deliveries_page_like_every_other_list()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, $$"""{ "items": [{{WebhookBody}}], "after": "next", "totalCount": null }""")
            .Respond(HttpStatusCode.OK, """
                {
                  "items": [{
                    "id": "6f2c25c1-0000-4000-8000-000000000009", "eventSource": "/tenants/t/sources/products", "eventType": "source_entry.added",
                    "eventId": "6f2c25c1-0000-4000-8000-00000000000a", "sequence": "00000000000000000042", "status": "DeadLettered",
                    "attemptCount": 8, "lastResponseStatus": 500, "lastError": "Internal Server Error",
                    "createdAt": "2026-11-01T10:00:00Z", "deliveredAt": null
                  }],
                  "after": null, "totalCount": null
                }
                """);
        using var client = Client(handler);
        var ct = TestContext.Current.CancellationToken;

        var destinations = (await client.Events.Destinations.List(new PageRequest { Limit = 10 }, ct)).Value;
        var deliveries = (await client.Events.Destinations.ListDeliveries(Destination, cancellationToken: ct)).Value;

        destinations.Items.ShouldHaveSingleItem().Filter.ToString().ShouldBe("type == 'source_entry.added'");
        destinations.Next.GetValueOrThrow().Value.ShouldBe("next");
        var delivery = deliveries.Items.ShouldHaveSingleItem();
        delivery.Status.GetValueOrThrow().ShouldBe(EventDeliveryStatus.DeadLettered);
        delivery.LastResponseStatus.GetValueOrThrow().ShouldBe(500);
        delivery.DeliveredAt.HasNoValue.ShouldBeTrue();
        handler.Requests[0].RequestUri!.Query.ShouldBe("?limit=10");
        handler.Requests[1].RequestUri!.AbsolutePath.ShouldBe($"/v1/event-destinations/{Id}/deliveries");
    }

    [Fact]
    public async Task ListTypes_reads_the_catalog_without_the_inline_schema()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            [{ "type": "card.updated", "version": "1", "description": "A card changed.",
               "schemaUrl": "https://api.occtoo.com/v1/events/schemas/card.updated/1",
               "filterableProperties": ["cardDefinitionId"], "schema": { "type": "object" } }]
            """);
        using var client = Client(handler);

        var types = (await client.Events.ListTypes(TestContext.Current.CancellationToken)).Value;

        var type = types.ShouldHaveSingleItem();
        type.Type.ShouldBe("card.updated");
        type.FilterableProperties.ShouldBe(["cardDefinitionId"]);
        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe("https://api.occtoo.com/v1/event-types");
    }
}
