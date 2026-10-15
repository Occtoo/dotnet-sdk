using System.Net;
using System.Text.Json;
using Occtoo.Assets;
using Occtoo.Sources;
using Shouldly;
using Xunit;
using static Occtoo.Sdk.Tests.Assets.AssetsTestSetup;

namespace Occtoo.Sdk.Tests.Assets;

public class AssetPropertiesTests
{
    private static EntryPropertyBuilder<AssetUpload> Chair => AssetUpload.FromBytes("chair", "chair.jpg", "12345"u8.ToArray());

    private static EntryPropertyBuilder<AssetUpload> Lamp => AssetUpload.FromBytes("lamp", "lamp.jpg", "12345"u8.ToArray());

    [Fact]
    public async Task Each_property_is_written_as_a_typed_ingest_row()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, Initialized(["chair"]));
        using var client = Client(api);

        AssetUpload chair = Chair
            .WithText("sku", "CH-001")
            .WithLocalizedText("title", "Blue chair", "en")
            .WithLocalizedText("title", "Blauer Stuhl", "de")
            .WithList("tags", "summer", "outdoor")
            .WithLocalizedList("features", ["stackable"], "en")
            .WithCleared("features", "de")
            .WithInteger("width", 42)
            .WithDecimal("price", 1299.5m)
            .WithBoolean("ok", true)
            .WithTimestamp("shot", DateTimeOffset.Parse("2026-09-30T10:00:00+02:00", null))
            .WithCleared("photographer");

        await client.Assets.Initialize(
            Media, [Initialization(chair)], cancellationToken: TestContext.Current.CancellationToken);

        api.Requests.Single().Body.ShouldBe(
            """{"folderId":null,"assets":[{"entryKey":"chair","filename":"chair.jpg","properties":[{"id":"sku","value":"CH-001"},{"id":"title","value":"Blue chair","language":"en"},{"id":"title","value":"Blauer Stuhl","language":"de"},{"id":"tags","value":["summer","outdoor"]},{"id":"features","value":["stackable"],"language":"en"},{"id":"features","value":null,"language":"de"},{"id":"width","value":42},{"id":"price","value":1299.5},{"id":"ok","value":true},{"id":"shot","value":"2026-09-30T10:00:00+02:00"},{"id":"photographer","value":null}]}]}""");
    }

    [Fact]
    public async Task Each_assets_rows_go_under_its_index_in_the_order_given()
    {
        var assets = await Send(
            Chair.WithText("sku", "CH-001"),
            Lamp
                .WithLocalizedText("title", "Lamp", "en")
                .WithLocalizedText("title", "Lampa", "sv")
                .WithText("sku", "LA-001"));

        assets[0].GetProperty("entryKey").GetString().ShouldBe("chair");
        assets[0].GetProperty("properties").GetRawText().ShouldBe("""[{"id":"sku","value":"CH-001"}]""");
        assets[1].GetProperty("entryKey").GetString().ShouldBe("lamp");
        assets[1].GetProperty("properties").GetRawText().ShouldBe(
            """[{"id":"title","value":"Lamp","language":"en"},{"id":"title","value":"Lampa","language":"sv"},{"id":"sku","value":"LA-001"}]""");
    }

    [Fact]
    public async Task Upload_writes_each_assets_rows_under_its_index_in_the_order_given()
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, Initialized(["lamp", "chair"]))
            .Respond(HttpStatusCode.OK, Completed(["lamp", "chair"]));
        using var blob = new StubHandler().RespondCreated().RespondCreated();
        using var client = Client(api, blob);

        await client.Assets.Upload(
            Media,
            [
                Lamp
                    .WithLocalizedText("title", "Lamp", "en")
                    .WithLocalizedText("title", "Lampa", "sv")
                    .WithText("sku", "LA-001"),
                Chair.WithText("sku", "CH-001"),
            ],
            options: new AssetUploadOptions { MaxConcurrentTransfers = 1 },
            cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(api.Requests[0].Body!);
        var assets = body.RootElement.GetProperty("assets");
        assets[0].GetProperty("entryKey").GetString().ShouldBe("lamp");
        assets[0].GetProperty("properties").GetRawText().ShouldBe(
            """[{"id":"title","value":"Lamp","language":"en"},{"id":"title","value":"Lampa","language":"sv"},{"id":"sku","value":"LA-001"}]""");
        assets[1].GetProperty("entryKey").GetString().ShouldBe("chair");
        assets[1].GetProperty("properties").GetRawText().ShouldBe("""[{"id":"sku","value":"CH-001"}]""");
    }

    [Fact]
    public async Task Integers_at_the_64_bit_limits_are_written_whole()
    {
        var properties = (await Send(Chair
                .WithInteger("highest", long.MaxValue)
                .WithInteger("lowest", long.MinValue)))[0]
            .GetProperty("properties");

        properties[0].GetProperty("value").GetInt64().ShouldBe(long.MaxValue);
        properties[1].GetProperty("value").GetInt64().ShouldBe(long.MinValue);
    }

    [Fact]
    public async Task An_asset_without_properties_has_no_properties_member()
    {
        var assets = await Send(Chair.WithText("sku", "CH-001"), Lamp);

        assets[0].TryGetProperty("properties", out _).ShouldBeTrue();
        assets[1].TryGetProperty("properties", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Initialize_without_properties_sends_the_body_it_always_sent()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, InitializedBody);
        using var client = Client(api);

        await client.Assets.Initialize(Media, [Logo], cancellationToken: TestContext.Current.CancellationToken);

        api.Requests.Single().Body.ShouldBe("""{"folderId":null,"assets":[{"entryKey":"logo","filename":"logo.png"}]}""");
    }

    [Fact]
    public async Task Property_ids_and_languages_are_written_normalized()
    {
        var properties = (await Send(Chair
                .WithText("SKU", "CH-001")
                .WithLocalizedText("Title", "Blå stol", "SV-se")))[0]
            .GetProperty("properties");

        properties.EnumerateArray().Select(row => row.GetProperty("id").GetString()).ShouldBe(["sku", "title"]);
        properties[1].GetProperty("language").GetString().ShouldBe("sv-SE");
    }

    [Fact]
    public async Task Upload_sends_properties_on_initialize_and_never_on_complete_or_uploadLinks()
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, ExpiredBody)
            .Respond(HttpStatusCode.OK, RefreshedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        var logo = AssetUpload.FromBytes("logo", "logo.png", "12345"u8.ToArray()).WithText("alt", "Company logo");

        var result = await client.Assets.Upload(
            Media, [logo], options: new AssetUploadOptions { MaxConcurrentTransfers = 1 }, cancellationToken: TestContext.Current.CancellationToken);

        result.Value.AllCompleted.ShouldBeTrue();
        api.Requests.Select(request => request.RequestUri!.AbsolutePath)
            .ShouldBe(["/v1/assets/media", "/v1/assets/media/uploadLinks", "/v1/assets/media/complete"]);

        HasProperties(api.Requests[0].Body!).ShouldBeTrue();
        HasProperties(api.Requests[1].Body!).ShouldBeFalse();
        HasProperties(api.Requests[2].Body!).ShouldBeFalse();

        static bool HasProperties(string body)
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("assets")[0].TryGetProperty("properties", out _);
        }
    }

    [Fact]
    public async Task A_plain_clear_is_written_as_a_null_row_without_a_language()
    {
        var properties = (await Send(Chair.WithProperty(new EntryProperty("width", PropertyValue.Clear))))[0]
            .GetProperty("properties");

        properties.GetRawText().ShouldBe("""[{"id":"width","value":null}]""");
    }

    [Fact]
    public async Task WithCleared_writes_one_null_row_per_language()
    {
        var properties = (await Send(Chair.WithCleared("title", "sv", "de")))[0]
            .GetProperty("properties");

        properties.GetRawText().ShouldBe(
            """[{"id":"title","value":null,"language":"sv"},{"id":"title","value":null,"language":"de"}]""");
    }

    [Fact]
    public async Task A_null_property_list_is_refused_under_its_index_without_a_round_trip()
    {
        var error = await Refused(Lamp, Chair.Build() with { Properties = null! });

        error.Failures.Keys.ShouldBe(["assets[1].properties"]);
    }

    [Fact]
    public async Task An_upload_with_a_null_property_list_fails_before_any_request()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media,
            [Lamp, Chair.Build() with { Properties = null! }],
            cancellationToken: TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Failures.Keys.ShouldBe(["assets[1].properties"]);
        api.RequestCount.ShouldBe(0);
        blob.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_rejected_property_keeps_Occtoos_path()
    {
        const string problem = """
            {
              "title": "One or more validation errors occurred.",
              "status": 400,
              "traceId": "00-abc-def-01",
              "errors": {
                "assets[0].properties[0].value": [ "Property 'sku' is Integer; a string does not match it." ]
              }
            }
            """;

        using var api = new StubHandler().Respond(HttpStatusCode.BadRequest, problem);
        using var client = Client(api);
        AssetUpload chair = Chair.WithText("sku", "CH-001");

        var result = await client.Assets.Initialize(
            Media, [Initialization(chair)], cancellationToken: TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Failures["assets[0].properties[0].value"].Single().ShouldContain("Integer");
    }

    /// <summary>Initializes the uploads and returns the <c>assets</c> array that was sent.</summary>
    private static async Task<JsonElement> Send(params IReadOnlyList<AssetUpload> uploads)
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, Initialized([.. uploads.Select(upload => upload.Key.Value)]));
        using var client = Client(api);

        var result = await client.Assets.Initialize(
            Media, [.. uploads.Select(Initialization)], cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var body = JsonDocument.Parse(api.Requests.Single().Body!);
        return body.RootElement.GetProperty("assets").Clone();
    }

    private static async Task<ValidationError> Refused(params IReadOnlyList<AssetUpload> uploads)
    {
        using var api = new StubHandler();
        using var client = Client(api);

        var result = await client.Assets.Initialize(
            Media, [.. uploads.Select(Initialization)], cancellationToken: TestContext.Current.CancellationToken);

        api.RequestCount.ShouldBe(0);
        return result.Error.ShouldBeOfType<ValidationError>();
    }

    private static AssetInitialization Initialization(AssetUpload upload) =>
        new(upload.Asset) { Properties = upload.Properties };
}
