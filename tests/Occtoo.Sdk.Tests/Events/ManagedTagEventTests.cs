using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSharpFunctionalExtensions;
using Occtoo.Authentication;
using Occtoo.Events;
using Occtoo.ManagedTags;
using Shouldly;
using Xunit;

namespace Occtoo.Sdk.Tests.Events;

public class ManagedTagEventTests
{
    private const string ListId = "42f3cce5-a2b8-4cf9-9c2e-8c07a2ced339";
    private const string ParentId = "42f3cce5-a2b8-4cf9-9c2e-8c07a2ced330";
    private const string ActorId = "c157d71e-b930-4eae-9c3d-23a8de1a1cbc";
    private const string Timestamp = "2026-11-05T09:15:12Z";

    [Theory]
    [InlineData("managed_tag_list.created", typeof(ManagedTagListCreated))]
    [InlineData("managed_tag_list.updated", typeof(ManagedTagListUpdated))]
    [InlineData("managed_tag_list.deleted", typeof(ManagedTagListDeleted))]
    [InlineData("managed_tag_value.added", typeof(ManagedTagValueAdded))]
    [InlineData("managed_tag_value.updated", typeof(ManagedTagValueUpdated))]
    [InlineData("managed_tag_value.deleted", typeof(ManagedTagValueDeleted))]
    public void Parses_all_six_types_from_strings_bytes_and_json_elements(string type, Type expected)
    {
        var json = Envelope(type).ToJsonString();
        var fromString = CloudEvent.Parse(json).Value;
        var fromBytes = CloudEvent.Parse(Encoding.UTF8.GetBytes(json)).Value;
        using var document = JsonDocument.Parse(json);
        var fromElement = CloudEvent.Parse(document.RootElement).Value;

        fromString.ShouldBeOfType(expected);
        fromBytes.ShouldBeOfType(expected);
        fromElement.ShouldBeOfType(expected);
        fromString.Type.ShouldBe(type);
        fromString.Source.ShouldBe($"/managed-tags/{ListId}");
        fromString.Subject.ShouldBe(type.StartsWith("managed_tag_list", StringComparison.Ordinal) ? ListId : "blue");
        fromString.Actor.GetValueOrThrow().ShouldBe(ActorId);
        fromString.CorrelationIds.ShouldBe([ActorId]);
        fromString.Time.GetValueOrThrow().ShouldBe(DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("managed_tag_list.created")]
    [InlineData("managed_tag_list.updated")]
    public void List_snapshots_preserve_metadata_without_shadowing_the_envelope_type(string type)
    {
        var parsed = CloudEvent.Parse(Envelope(type).ToJsonString()).Value;
        var actual = parsed switch
        {
            ManagedTagListCreated e => (e.ManagedTagId, e.DisplayName, e.TagType, e.ParentId, e.CreatedAt, e.LastModifiedAt, e.CreatedBy, e.UpdatedBy),
            ManagedTagListUpdated e => (e.ManagedTagId, e.DisplayName, e.TagType, e.ParentId, e.CreatedAt, e.LastModifiedAt, e.CreatedBy, e.UpdatedBy),
            _ => throw new InvalidOperationException(),
        };

        actual.ManagedTagId.Value.ShouldBe(Guid.Parse(ListId));
        actual.DisplayName.ShouldBe("Colours");
        actual.TagType.GetValueOrThrow().ShouldBe(ManagedTagType.LocalizedText);
        actual.ParentId.GetValueOrThrow().Value.ShouldBe(Guid.Parse(ParentId));
        actual.CreatedAt.ShouldBe(DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture));
        actual.LastModifiedAt.ShouldBe(DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture));
        actual.CreatedBy.GetValueOrThrow().ShouldBe(Guid.Parse(ActorId));
        actual.UpdatedBy.GetValueOrThrow().ShouldBe(Guid.Parse(ActorId));
        parsed.Type.ShouldBe(type);
    }

    [Theory]
    [InlineData("managed_tag_value.added", false)]
    [InlineData("managed_tag_value.updated", false)]
    [InlineData("managed_tag_value.added", true)]
    [InlineData("managed_tag_value.updated", true)]
    public void Value_snapshots_preserve_text_translations_and_metadata_after_document_disposal(string type, bool localized)
    {
        var envelope = Envelope(type);
        envelope["data"]!["value"] = localized ? JsonNode.Parse("""{"en":"Blue","sv":"Blå","custom-language":"Colour"}""") : JsonValue.Create("");
        envelope["data"]!["key"] = "Blue/path\\key";
        var parsed = CloudEvent.Parse(envelope.ToJsonString()).Value;
        var actual = parsed switch
        {
            ManagedTagValueAdded e => (e.ManagedTagId, e.Key, e.Content, e.Order, e.ParentKey, e.CreatedAt, e.LastModifiedAt, e.CreatedBy, e.UpdatedBy, e.ValueTruncated),
            ManagedTagValueUpdated e => (e.ManagedTagId, e.Key, e.Content, e.Order, e.ParentKey, e.CreatedAt, e.LastModifiedAt, e.CreatedBy, e.UpdatedBy, e.ValueTruncated),
            _ => throw new InvalidOperationException(),
        };

        actual.ManagedTagId.Value.ShouldBe(Guid.Parse(ListId));
        actual.Key.ShouldBe("Blue/path\\key");
        actual.Order.ShouldBe(2.5);
        actual.ParentKey.GetValueOrThrow().ShouldBe("parent/path");
        actual.CreatedAt.ShouldBe(DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture));
        actual.LastModifiedAt.GetValueOrThrow().ShouldBe(DateTimeOffset.Parse(Timestamp, CultureInfo.InvariantCulture));
        actual.CreatedBy.ShouldBe(Guid.Parse(ActorId));
        actual.UpdatedBy.GetValueOrThrow().ShouldBe(Guid.Parse(ActorId));
        actual.ValueTruncated.ShouldBeFalse();
        if (localized)
            actual.Content.GetValueOrThrow().ShouldBeOfType<ManagedTagValueContent.LocalizedValue>()
                .Translations.ShouldBe(new Dictionary<string, string> { ["en"] = "Blue", ["sv"] = "Blå", ["custom-language"] = "Colour" });
        else
            actual.Content.GetValueOrThrow().ShouldBeOfType<ManagedTagValueContent.TextValue>().Value.ShouldBe("");
    }

    [Theory]
    [InlineData("managed_tag_value.added")]
    [InlineData("managed_tag_value.updated")]
    public void Truncated_values_and_absent_optional_fields_remain_absent(string type)
    {
        var envelope = Envelope(type);
        envelope["data"] = JsonNode.Parse($$"""
            { "managedTagId":"{{ListId}}", "key":"blue", "order":2.5,
              "createdAt":"{{Timestamp}}", "createdBy":"{{ActorId}}", "valueTruncated":true }
            """);
        var evt = CloudEvent.Parse(envelope.ToJsonString()).Value;
        var actual = evt switch
        {
            ManagedTagValueAdded e => (e.Content, e.ParentKey, e.LastModifiedAt, e.UpdatedBy, e.ValueTruncated, e.Order),
            ManagedTagValueUpdated e => (e.Content, e.ParentKey, e.LastModifiedAt, e.UpdatedBy, e.ValueTruncated, e.Order),
            _ => throw new InvalidOperationException(),
        };
        actual.Content.HasNoValue.ShouldBeTrue();
        actual.ParentKey.HasNoValue.ShouldBeTrue();
        actual.LastModifiedAt.HasNoValue.ShouldBeTrue();
        actual.UpdatedBy.HasNoValue.ShouldBeTrue();
        actual.ValueTruncated.ShouldBeTrue();
        actual.Order.ShouldBe(2.5);
    }

    [Fact]
    public void Unknown_list_kind_and_null_optional_metadata_do_not_fail_the_event()
    {
        var envelope = Envelope("managed_tag_list.updated");
        envelope["data"]!["type"] = "FutureKind";
        envelope["data"]!["parentId"] = null;
        envelope["data"]!["createdBy"] = null;
        envelope["data"]!["updatedBy"] = null;
        var evt = CloudEvent.Parse(envelope.ToJsonString()).Value.ShouldBeOfType<ManagedTagListUpdated>();
        evt.TagType.HasNoValue.ShouldBeTrue();
        evt.ParentId.HasNoValue.ShouldBeTrue();
        evt.CreatedBy.HasNoValue.ShouldBeTrue();
        evt.UpdatedBy.HasNoValue.ShouldBeTrue();
    }

    [Theory]
    [InlineData("managed_tag_list.deleted")]
    [InlineData("managed_tag_value.deleted")]
    public void Deletions_need_only_their_identifiers(string type)
    {
        var envelope = Envelope(type);
        envelope["data"] = JsonNode.Parse($$"""{ "managedTagId":"{{ListId}}", "key":"blue" }""");
        var evt = CloudEvent.Parse(envelope.ToJsonString()).Value;
        (evt switch
        {
            ManagedTagListDeleted e => e.ManagedTagId,
            ManagedTagValueDeleted e => e.ManagedTagId,
            _ => throw new InvalidOperationException(),
        }).Value.ShouldBe(Guid.Parse(ListId));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("{\"en\":null}")]
    public void Malformed_content_returns_a_failure(string value)
    {
        var envelope = Envelope("managed_tag_value.updated");
        envelope["data"]!["value"] = JsonNode.Parse(value);
        var result = CloudEvent.Parse(envelope.ToJsonString());
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Invalid_list_identity_returns_a_failure_without_throwing(string? id)
    {
        var envelope = Envelope("managed_tag_list.deleted");
        envelope["data"]!["managedTagId"] = id;
        CloudEvent.Parse(envelope.ToJsonString()).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Filters_combine_list_and_value_families_and_narrow_value_keys()
    {
        var id = ManagedTagId.From(Guid.Parse(ListId));
        var filter = EventFilter.OfType<ManagedTagListEvent>(e => e.WithManagedTag(id))
            .OrType<ManagedTagValueEvent>(e => e.WithManagedTag(id));
        filter.ToString().ShouldBe($"((type eq \"managed_tag_list.created\" or type eq \"managed_tag_list.deleted\" or type eq \"managed_tag_list.updated\") and managedTagId eq \"{ListId}\") or ((type eq \"managed_tag_value.added\" or type eq \"managed_tag_value.deleted\" or type eq \"managed_tag_value.updated\") and managedTagId eq \"{ListId}\")");
        EventFilter.OfType<ManagedTagValueUpdated>(e => e.WithManagedTag(id).WithSubject("Blue/\"key\"\\path"))
            .ToString().ShouldBe($"type eq \"managed_tag_value.updated\" and managedTagId eq \"{ListId}\" and subject eq \"Blue/\\\"key\\\"\\\\path\"");
        EventFilter.OfType<ManagedTagListDeleted>(e => e.WithManagedTag(id, ManagedTagId.From(Guid.Parse(ParentId))))
            .ToString().ShouldContain($"(managedTagId eq \"{ListId}\" or managedTagId eq \"{ParentId}\")");
    }

    [Fact]
    public async Task All_event_readers_use_the_managed_tag_parser()
    {
        var json = Envelope("managed_tag_value.updated").ToJsonString();
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, $$"""{"items":[{{json}}],"hasMore":false}""")
            .Respond(HttpStatusCode.OK, $$"""{"leaseId":"{{ParentId}}","generation":0,"attempt":1,"events":[{{json}}]}""")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"data: {json}\n\n", Encoding.UTF8, "text/event-stream"),
            });
        using var client = new OcctooClient(new HttpClient(handler), new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });
        var ct = TestContext.Current.CancellationToken;
        var page = await client.Events.Pull(cancellationToken: ct);
        page.Value.Items.Single().ShouldBeOfType<ManagedTagValueUpdated>();
        var batch = await client.Events.Batch.Pull(EventDestinationId.From(Guid.Parse(ParentId)), "worker-1", cancellationToken: ct);
        batch.Value.GetValueOrThrow().Events.Single().ShouldBeOfType<ManagedTagValueUpdated>();
        await using var stream = client.Events.Stream(cancellationToken: ct).GetAsyncEnumerator(ct);
        (await stream.MoveNextAsync()).ShouldBeTrue();
        stream.Current.ShouldBeOfType<ManagedTagValueUpdated>();
    }

    private static JsonObject Envelope(string type) => JsonNode.Parse($$"""
        {
          "type":"{{type}}", "sequence":"003.00000000000000184467",
          "source":"/managed-tags/{{ListId}}", "subject":"{{(type.StartsWith("managed_tag_list", StringComparison.Ordinal) ? ListId : "blue")}}",
          "time":"{{Timestamp}}",
          "data": { "managedTagId":"{{ListId}}", "displayName":"Colours", "type":"LocalizedText",
            "parentId":"{{ParentId}}", "key":"blue", "value":"Blue", "order":2.5, "parentKey":"parent/path",
            "createdAt":"{{Timestamp}}", "lastModifiedAt":"{{Timestamp}}", "createdBy":"{{ActorId}}", "updatedBy":"{{ActorId}}",
            "valueTruncated":false, "correlationIds":["{{ActorId}}"], "actor":{"id":"{{ActorId}}"} }
        }
        """)!.AsObject();
}
