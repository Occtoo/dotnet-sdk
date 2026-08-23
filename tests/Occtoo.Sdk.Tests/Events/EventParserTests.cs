using System.Text.Json;
using Occtoo.Events;
using Occtoo.Events.Internal;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Events;

public class EventParserTests
{
    private static CloudEvent Parse(string envelopeJson)
    {
        using var document = JsonDocument.Parse(envelopeJson);
        var parsed = EventParser.Parse(document.RootElement);
        parsed.IsSuccess.ShouldBeTrue(parsed.IsFailure ? parsed.Error.Message : "");
        return parsed.Value;
    }

    [Fact]
    public void Maps_the_full_envelope_and_a_source_entry_payload()
    {
        var evt = Parse("""
            {
              "specversion": "1.0",
              "id": "9f4f2c74-6a3e-4a5b-9a2f-3e6f0d1c2b3a",
              "type": "source_entry.updated",
              "sequence": "0000000000000042",
              "source": "/sources/products",
              "subject": "sku-123",
              "time": "2026-08-01T10:00:00Z",
              "datacontenttype": "application/json",
              "data": {
                "sourceId": "products",
                "entryKey": "sku-123",
                "version": 7,
                "changedProperties": ["name", "price"],
                "valuesTruncated": false,
                "values": {
                  "name": [
                    { "value": "Blue chair", "language": "en" },
                    { "value": "Blå stol", "language": "sv" }
                  ],
                  "price": [
                    { "value": "100.111" }
                  ]
                },
                "correlationIds": ["corr-1"],
                "actor": { "id": "user-9", "type": "user" }
              }
            }
            """);

        var updated = evt.ShouldBeOfType<SourceEntryUpdated>();
        updated.Id.ShouldBe(Guid.Parse("9f4f2c74-6a3e-4a5b-9a2f-3e6f0d1c2b3a"));
        updated.Type.ShouldBe("source_entry.updated");
        updated.Sequence.Value.ShouldBe("0000000000000042");
        updated.Source.ShouldBe("/sources/products");
        updated.Subject.ShouldBe("sku-123");
        updated.Time.GetValueOrDefault().ShouldBe(DateTimeOffset.Parse("2026-08-01T10:00:00Z", null));
        updated.CorrelationIds.ShouldBe(["corr-1"]);
        updated.Actor.GetValueOrDefault().ShouldBe("user-9");

        updated.SourceId.Value.ShouldBe("products");
        updated.EntryKey.Value.ShouldBe("sku-123");
        updated.Version.ShouldBe(7);
        updated.ChangedProperties.Select(p => p.Value).ShouldBe(["name", "price"]);
        updated.ValuesTruncated.ShouldBeFalse();

        var values = updated.Values.GetValueOrThrow();
        values["name"][0].Value.ShouldBe("Blue chair");
        values["name"][0].Language.GetValueOrThrow().Value.ShouldBe("en");
        values["name"][1].Language.GetValueOrThrow().Value.ShouldBe("sv");
        values["price"][0].Language.HasNoValue.ShouldBeTrue();
    }

    [Fact]
    public void Optional_envelope_fields_are_absent_not_defaulted()
    {
        var evt = Parse("""
            {
              "id": "not-a-guid",
              "type": "source.created",
              "sequence": "0000000000000001",
              "data": { "sourceId": "products" }
            }
            """);

        var created = evt.ShouldBeOfType<SourceCreated>();
        created.Time.HasNoValue.ShouldBeTrue();
        created.Actor.HasNoValue.ShouldBeTrue();
        created.CorrelationIds.ShouldBeEmpty();
        created.SourceId.Value.ShouldBe("products");
    }

    [Fact]
    public void Maps_source_updated_property_changes()
    {
        var evt = Parse("""
            {
              "id": "c0ffee00-0000-0000-0000-000000000001",
              "type": "source.updated",
              "sequence": "0000000000000002",
              "data": {
                "sourceId": "products",
                "changes": ["properties"],
                "properties": [
                  { "id": "name", "change": "added" },
                  { "id": "price", "change": "removed" }
                ]
              }
            }
            """);

        var updated = evt.ShouldBeOfType<SourceUpdated>();
        updated.Changes.ShouldBe(["properties"]);
        var properties = updated.Properties.GetValueOrThrow();
        properties.Count.ShouldBe(2);
        properties[0].Id.Value.ShouldBe("name");
        properties[0].Change.ShouldBe("added");
        properties[1].Change.ShouldBe("removed");
    }

    [Fact]
    public void Keeps_the_raw_properties_of_an_added_entry()
    {
        var evt = Parse("""
            {
              "id": "c0ffee00-0000-0000-0000-000000000002",
              "type": "source_entry.added",
              "sequence": "0000000000000003",
              "data": {
                "sourceId": "products",
                "entryKey": "sku-1",
                "version": 1,
                "properties": { "name": [{ "value": "Chair" }] }
              }
            }
            """);

        var added = evt.ShouldBeOfType<SourceEntryAdded>();
        added.Properties.GetValueOrThrow()
            .GetProperty("name")[0].GetProperty("value").GetString().ShouldBe("Chair");
    }

    [Fact]
    public void Maps_a_segment_membership_change()
    {
        var evt = Parse("""
            {
              "id": "c0ffee00-0000-0000-0000-000000000003",
              "type": "card.segment_membership_changed",
              "sequence": "0000000000000004",
              "data": {
                "cardDefinitionId": "cards",
                "cardId": "card-1",
                "segmentId": "summer-sale",
                "change": "entered"
              }
            }
            """);

        var changed = evt.ShouldBeOfType<CardSegmentMembershipChanged>();
        changed.CardDefinitionId.Value.ShouldBe("cards");
        changed.CardId.Value.ShouldBe("card-1");
        changed.SegmentId.Value.ShouldBe("summer-sale");
        changed.Change.ShouldBe("entered");
    }

    [Fact]
    public void Maps_a_segment_event_with_and_without_a_card_definition()
    {
        var bound = Parse("""
            {
              "id": "c0ffee00-0000-0000-0000-000000000004",
              "type": "segment.updated",
              "sequence": "0000000000000005",
              "data": {
                "segmentId": "summer-sale",
                "segmentDefinitionId": "seasonal",
                "cardDefinitionId": "cards",
                "changes": ["rules"]
              }
            }
            """).ShouldBeOfType<SegmentUpdated>();

        bound.CardDefinitionId.GetValueOrThrow().Value.ShouldBe("cards");
        bound.Changes.ShouldBe(["rules"]);

        var unbound = Parse("""
            {
              "id": "c0ffee00-0000-0000-0000-000000000005",
              "type": "segment.created",
              "sequence": "0000000000000006",
              "data": { "segmentId": "summer-sale", "segmentDefinitionId": "seasonal" }
            }
            """).ShouldBeOfType<SegmentCreated>();

        unbound.CardDefinitionId.HasNoValue.ShouldBeTrue();
    }

    [Fact]
    public void Maps_a_destination_entry_event()
    {
        var evt = Parse("""
            {
              "id": "c0ffee00-0000-0000-0000-000000000006",
              "type": "destination_entry.deleted",
              "sequence": "0000000000000007",
              "data": {
                "destinationId": "webshop",
                "apiVersionId": "av-1",
                "apiVersion": "v1",
                "endpointId": "ep-1",
                "endpoint": "products",
                "entryId": "sku-1",
                "version": "12"
              }
            }
            """);

        var deleted = evt.ShouldBeOfType<DestinationEntryDeleted>();
        deleted.DestinationId.Value.ShouldBe("webshop");
        deleted.ApiVersionId.Value.ShouldBe("av-1");
        deleted.ApiVersion.ShouldBe("v1");
        deleted.EndpointId.Value.ShouldBe("ep-1");
        deleted.Endpoint.ShouldBe("products");
        deleted.EntryId.ShouldBe("sku-1");
        deleted.Version.ShouldBe(12); // string-typed versions parse too
    }

    [Fact]
    public void Maps_an_endpoint_event()
    {
        var evt = Parse("""
            {
              "id": "c0ffee00-0000-0000-0000-000000000007",
              "type": "endpoint.created",
              "sequence": "0000000000000008",
              "data": {
                "destinationId": "webshop",
                "apiVersionId": "av-1",
                "apiVersion": "v1",
                "endpointId": "ep-1"
              }
            }
            """);

        var created = evt.ShouldBeOfType<EndpointCreated>();
        created.DestinationId.Value.ShouldBe("webshop");
        created.EndpointId.Value.ShouldBe("ep-1");
    }

    [Fact]
    public void Maps_a_user_event_with_optional_fields()
    {
        var evt = Parse("""
            {
              "id": "c0ffee00-0000-0000-0000-000000000008",
              "type": "user.created",
              "sequence": "0000000000000009",
              "data": { "userId": "user-9", "email": "ada@example.com" }
            }
            """);

        var created = evt.ShouldBeOfType<UserCreated>();
        created.UserId.Value.ShouldBe("user-9");
        created.Email.GetValueOrDefault().ShouldBe("ada@example.com");
        created.FirstName.HasNoValue.ShouldBeTrue();
        created.LastName.HasNoValue.ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_type_becomes_an_UnknownEvent_with_the_raw_payload()
    {
        var evt = Parse("""
            {
              "id": "c0ffee00-0000-0000-0000-000000000009",
              "type": "warehouse.opened",
              "sequence": "0000000000000010",
              "data": { "warehouseId": "north" }
            }
            """);

        var unknown = evt.ShouldBeOfType<UnknownEvent>();
        unknown.Type.ShouldBe("warehouse.opened");
        unknown.Sequence.Value.ShouldBe("0000000000000010");
        unknown.Data.GetProperty("warehouseId").GetString().ShouldBe("north");
    }

    [Theory]
    [InlineData("""{ "id": "x", "sequence": "0000000000000011" }""")]
    [InlineData("""{ "id": "x", "type": "source.created" }""")]
    public void An_envelope_without_type_or_sequence_is_a_failure(string envelopeJson)
    {
        using var document = JsonDocument.Parse(envelopeJson);
        var parsed = EventParser.Parse(document.RootElement);
        parsed.IsFailure.ShouldBeTrue();
        parsed.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Fact]
    public void Every_cataloged_type_materializes_as_its_record()
    {
        foreach (var (name, type) in EventTypeCatalog.ByName)
        {
            var evt = Parse($$"""
                {
                  "id": "c0ffee00-0000-0000-0000-00000000000a",
                  "type": "{{name}}",
                  "sequence": "0000000000000012",
                  "data": {}
                }
                """);

            evt.ShouldBeOfType(type);
        }
    }
}
