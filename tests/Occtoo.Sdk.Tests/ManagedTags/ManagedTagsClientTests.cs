using System.Net;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Occtoo.Authentication;
using Occtoo.ManagedTags;
using Shouldly;
using Vogen;
using Xunit;

namespace Occtoo.Sdk.Tests.ManagedTags;

public class ManagedTagsClientTests
{
    private const string TagBody = """
        {
          "id": "6f1c2d3e-4a5b-4c6d-8e7f-9a0b1c2d3e4f",
          "displayName": "Colors",
          "type": "LocalizedText",
          "parentId": null,
          "createdAt": "2026-09-01T10:00:00Z",
          "lastModifiedAt": null,
          "createdBy": "9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a",
          "updatedBy": null
        }
        """;

    private const string ValueBody = """
        {
          "key": "blue",
          "value": { "en": "Blue", "sv": "Blå" },
          "order": 1,
          "parentKey": "cool",
          "createdAt": "2026-09-01T10:00:00Z",
          "lastModifiedAt": "2026-09-02T10:00:00Z",
          "createdBy": "9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a",
          "updatedBy": "9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a"
        }
        """;

    private static readonly ManagedTagId Colors = ManagedTagId.From(Guid.Parse("6f1c2d3e-4a5b-4c6d-8e7f-9a0b1c2d3e4f"));

    private static OcctooClient Client(StubHandler handler) =>
        new(new HttpClient(handler), new OcctooClientOptions
        {
            Credential = OcctooCredential.ApiKey(ApiKey.From("key-1")),
        });

    [Fact]
    public async Task List_carries_filters_and_maps_the_page()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, $$"""
            { "items": [{{TagBody}}], "after": "c1", "totalCount": null }
            """);
        using var client = Client(handler);

        var result = await client.ManagedTags.List(new ManagedTagListQuery
        {
            Name = "col",
            Type = ManagedTagType.LocalizedText,
            ParentId = Colors,
            Page = new PageRequest { Limit = 10 },
        }, TestContext.Current.CancellationToken);

        var tag = result.Value.Items.ShouldHaveSingleItem();
        tag.Id.ShouldBe(Colors);
        tag.Type.ShouldBe(ManagedTagType.LocalizedText);
        tag.ParentId.HasNoValue.ShouldBeTrue();
        tag.LastModifiedAt.HasNoValue.ShouldBeTrue();
        tag.CreatedBy.GetValueOrThrow().ShouldBe(Guid.Parse("9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a"));

        handler.Requests.Single().RequestUri!.AbsoluteUri.ShouldBe(
            "https://api.occtoo.com/v1/managed-tags?name=col&type=LocalizedText"
            + "&parentId=6f1c2d3e-4a5b-4c6d-8e7f-9a0b1c2d3e4f&limit=10");
    }

    [Fact]
    public async Task Create_posts_the_tag_and_Update_puts_the_replacement()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Created, TagBody)
            .Respond(HttpStatusCode.OK, TagBody);
        using var client = Client(handler);

        var created = await client.ManagedTags.Create(
            new CreateManagedTag("Colors", ManagedTagType.LocalizedText), TestContext.Current.CancellationToken);
        created.Value.DisplayName.ShouldBe("Colors");
        handler.Requests[0].Body.ShouldBe("""{"displayName":"Colors","type":"LocalizedText"}""");

        await client.ManagedTags.Update(
            Colors, new UpdateManagedTag("Colours") { ParentId = Colors }, TestContext.Current.CancellationToken);
        handler.Requests[1].Method.ShouldBe(HttpMethod.Put);
        handler.Requests[1].Body.ShouldBe(
            """{"displayName":"Colours","parentId":"6f1c2d3e-4a5b-4c6d-8e7f-9a0b1c2d3e4f"}""");
    }

    [Fact]
    public async Task Delete_is_a_conflict_while_a_card_definition_references_the_tag()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Conflict, """{ "title": "referenced by a card definition" }""")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = Client(handler);

        var blocked = await client.ManagedTags.Delete(Colors, TestContext.Current.CancellationToken);
        blocked.Error.ShouldBeOfType<ConflictError>();

        var deleted = await client.ManagedTags.Delete(Colors, TestContext.Current.CancellationToken);
        deleted.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task GetValue_maps_localized_content_and_parent_key()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, ValueBody);
        using var client = Client(handler);

        var result = await client.ManagedTags.GetValue(Colors, "blue", TestContext.Current.CancellationToken);

        var value = result.Value;
        value.Key.Value.ShouldBe("blue");
        var localized = value.Content.ShouldBeOfType<ManagedTagValueContent.LocalizedValue>();
        localized.Translations["sv"].ShouldBe("Blå");
        value.Order.ShouldBe(1);
        value.ParentKey.GetValueOrThrow().Value.ShouldBe("cool");
        value.UpdatedBy.HasValue.ShouldBeTrue();

        handler.Requests.Single().RequestUri!.AbsoluteUri
            .ShouldBe("https://api.occtoo.com/v1/managed-tags/6f1c2d3e-4a5b-4c6d-8e7f-9a0b1c2d3e4f/values/blue");
    }

    [Fact]
    public async Task A_text_tag_value_maps_to_text_content()
    {
        using var handler = new StubHandler().Respond(HttpStatusCode.OK, """
            { "key": "s", "value": "Small", "order": 0, "parentKey": null,
              "createdAt": "2026-09-01T10:00:00Z", "lastModifiedAt": null,
              "createdBy": "9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a", "updatedBy": null }
            """);
        using var client = Client(handler);

        var result = await client.ManagedTags.GetValue(Colors, "s", TestContext.Current.CancellationToken);

        result.Value.Content.ShouldBe(ManagedTagValueContent.Text("Small"));
        result.Value.ParentKey.HasNoValue.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateValue_sends_exactly_one_content_representation()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.Created, ValueBody)
            .Respond(HttpStatusCode.Created, ValueBody);
        using var client = Client(handler);

        await client.ManagedTags.CreateValue(Colors, new CreateManagedTagValue("blue",
            ManagedTagValueContent.Localized(new Dictionary<string, string> { ["en"] = "Blue", ["sv"] = "Blå" }))
        {
            Order = 1,
            ParentKey = ManagedTagValueKey.From("cool"),
        }, TestContext.Current.CancellationToken);

        await client.ManagedTags.CreateValue(Colors, new CreateManagedTagValue("s", "Small"), TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.AbsoluteUri
            .ShouldBe("https://api.occtoo.com/v1/managed-tags/6f1c2d3e-4a5b-4c6d-8e7f-9a0b1c2d3e4f/values");
        using var localizedBody = JsonDocument.Parse(handler.Requests[0].Body!);
        var localizedRoot = localizedBody.RootElement;
        localizedRoot.GetProperty("key").GetString().ShouldBe("blue");
        localizedRoot.GetProperty("value").TryGetProperty("singleValue", out _).ShouldBeFalse();
        localizedRoot.GetProperty("value").GetProperty("localizedValue").GetProperty("sv").GetString().ShouldBe("Blå");
        localizedRoot.GetProperty("order").GetDouble().ShouldBe(1);
        localizedRoot.GetProperty("parentKey").GetString().ShouldBe("cool");
        handler.Requests[1].Body.ShouldBe("""{"key":"s","value":{"singleValue":"Small"},"order":0}""");
    }

    [Fact]
    public async Task UpdateValue_and_DeleteValue_escape_the_key_in_the_route()
    {
        using var handler = new StubHandler()
            .Respond(HttpStatusCode.OK, ValueBody)
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = Client(handler);

        await client.ManagedTags.UpdateValue(Colors, "sky blue", new UpdateManagedTagValue("Sky"), TestContext.Current.CancellationToken);
        await client.ManagedTags.DeleteValue(Colors, "sky blue", TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.AbsoluteUri.ShouldEndWith("/values/sky%20blue");
        handler.Requests[0].Method.ShouldBe(HttpMethod.Put);
        handler.Requests[1].Method.ShouldBe(HttpMethod.Delete);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("tab\there")]
    public void Value_keys_must_be_a_single_path_segment(string key)
    {
        Should.Throw<ValueObjectValidationException>(() => ManagedTagValueKey.From(key));
    }
}
