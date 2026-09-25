using System.Net;
using System.Text.Json;
using Occtoo.Assets;
using Shouldly;
using Xunit;
using static Occtoo.Sdk.Tests.Assets.AssetsTestSetup;

namespace Occtoo.Sdk.Tests.Assets;

public class AssetsClientTests
{
    [Fact]
    public async Task Initialize_posts_the_assets_and_returns_a_link_per_key()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, InitializedBody);
        using var client = Client(api);

        var result = await client.Assets.Initialize(Media, [Logo], default, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var link = result.Value.Succeeded[AssetKey.From("logo")];
        link.Url.ShouldBe(new Uri(UploadUrl));
        link.Filename.Value.ShouldBe("logo.png");
        link.ExpiresAt.ShouldBe(DateTimeOffset.Parse(LinkExpiry, null));
        result.Value.AllSucceeded.ShouldBeTrue();

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media");

        using var body = JsonDocument.Parse(request.Body!);
        var asset = body.RootElement.GetProperty("assets")[0];
        asset.GetProperty("entryKey").GetString().ShouldBe("logo");
        asset.GetProperty("filename").GetString().ShouldBe("logo.png");
        body.RootElement.GetProperty("folderId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Initialize_sends_the_folder_when_one_was_given()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, InitializedBody);
        using var client = Client(api);
        var folder = FolderId.From(Guid.Parse("2f1d4a3c-0000-4000-8000-000000000001"));

        await client.Assets.Initialize(Media, [Logo], folder, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(api.Requests.Single().Body!);
        body.RootElement.GetProperty("folderId").GetGuid().ShouldBe(folder.Value);
    }

    [Fact]
    public async Task A_key_occtoo_refused_lands_in_the_failed_map_with_its_reason()
    {
        const string partial = """
            {
              "initialized": {},
              "failed": { "logo": "Asset already completed" }
            }
            """;

        using var api = new StubHandler().Respond(HttpStatusCode.OK, partial);
        using var client = Client(api);

        var result = await client.Assets.Initialize(Media, [Logo], default, TestContext.Current.CancellationToken);

        result.Value.Succeeded.ShouldBeEmpty();
        result.Value.Failed[AssetKey.From("logo")].ShouldBe("Asset already completed");
        result.Value.AllSucceeded.ShouldBeFalse();
        result.Value.Find(AssetKey.From("logo")).HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task RefreshUploadLinks_posts_to_the_uploadLinks_route()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, RefreshedBody);
        using var client = Client(api);

        var result = await client.Assets.RefreshUploadLinks(
            Media, [Logo], TestContext.Current.CancellationToken);

        result.Value.Succeeded[AssetKey.From("logo")].Url.ShouldBe(new Uri(RefreshedUploadUrl));
        api.Requests.Single().RequestUri!.ToString()
            .ShouldBe("https://api.occtoo.com/v1/assets/media/uploadLinks");
    }

    [Fact]
    public async Task Complete_posts_to_the_complete_route_and_returns_the_file()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, CompletedBody);
        using var client = Client(api);

        var result = await client.Assets.Complete(Media, [Logo], TestContext.Current.CancellationToken);

        var file = result.Value.Succeeded[AssetKey.From("logo")];
        file.MimeType.ShouldBe("image/png");
        file.Size.ShouldBe(5);
        file.PublicUrl.ShouldBe(new Uri("https://cdn.occtoo.com/media/logo.png"));
        file.Width.GetValueOrDefault().ShouldBe(64);
        file.Height.GetValueOrDefault().ShouldBe(32);

        api.Requests.Single().RequestUri!.ToString()
            .ShouldBe("https://api.occtoo.com/v1/assets/media/complete");
    }

    [Fact]
    public async Task A_completed_file_with_no_dimensions_carries_none()
    {
        const string withoutDimensions = """
            {
              "completed": {
                "logo": {
                  "filename": "note.txt",
                  "mimeType": "text/plain",
                  "size": 3,
                  "publicUrl": "https://cdn.occtoo.com/media/note.txt"
                }
              },
              "failed": {}
            }
            """;

        using var api = new StubHandler().Respond(HttpStatusCode.OK, withoutDimensions);
        using var client = Client(api);

        var result = await client.Assets.Complete(Media, [Logo], TestContext.Current.CancellationToken);

        var file = result.Value.Succeeded[AssetKey.From("logo")];
        file.Width.HasValue.ShouldBeFalse();
        file.Height.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task GetState_reads_the_keys_from_the_query_string()
    {
        const string states = """
            {
              "logo": {
                "status": "Completed",
                "filename": "logo.png",
                "mimeType": "image/png",
                "size": 5,
                "publicUrl": "https://cdn.occtoo.com/media/logo.png"
              }
            }
            """;

        using var api = new StubHandler().Respond(HttpStatusCode.OK, states);
        using var client = Client(api);

        var result = await client.Assets.GetState(
            Media,
            [AssetKey.From("logo"), AssetKey.From("note_1")],
            TestContext.Current.CancellationToken);

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media?key=logo&key=note_1");

        result.Value[AssetKey.From("logo")].Status.ShouldBe(AssetStatus.Completed);
        result.Value[AssetKey.From("logo")].Filename.GetValueOrDefault().Value.ShouldBe("logo.png");

        // A key nothing initialized is simply absent.
        result.Value.ContainsKey(AssetKey.From("note_1")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_status_this_sdk_does_not_know_reads_as_absent()
    {
        const string states = """
            { "logo": { "status": "Quarantined" } }
            """;

        using var api = new StubHandler().Respond(HttpStatusCode.OK, states);
        using var client = Client(api);

        var result = await client.Assets.GetState(
            Media, [AssetKey.From("logo")], TestContext.Current.CancellationToken);

        result.Value[AssetKey.From("logo")].Status.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_sends_the_keys_as_a_delete_and_returns_nothing()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.NoContent, EmptyBody);
        using var client = Client(api);

        var result = await client.Assets.Delete(
            Media, [AssetKey.From("logo")], TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Delete);
        request.RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media?key=logo");
    }

    [Fact]
    public async Task A_rejection_arrives_as_the_problem_detail()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.BadRequest, RejectedBody);
        using var client = Client(api);

        var result = await client.Assets.Initialize(Media, [Logo], default, TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Message.ShouldContain("Media data sources");
        error.Failures.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(AuthenticationError))]
    [InlineData(HttpStatusCode.Forbidden, typeof(ForbiddenError))]
    [InlineData(HttpStatusCode.NotFound, typeof(NotFoundError))]
    [InlineData(HttpStatusCode.Conflict, typeof(ConflictError))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(ServerError))]
    public async Task Each_documented_status_maps_to_its_error_type(HttpStatusCode status, Type expected)
    {
        using var api = new StubHandler().Respond(status, RejectedBody);
        using var client = Client(api);

        var result = await client.Assets.Initialize(Media, [Logo], default, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType(expected);
    }

    [Fact]
    public async Task A_missing_data_source_answers_409_and_therefore_a_conflict_not_a_not_found()
    {
        const string missing = """
            {
              "title": "Conflict",
              "status": 409,
              "detail": "Data source 'media' does not exist"
            }
            """;

        using var api = new StubHandler().Respond(HttpStatusCode.Conflict, missing);
        using var client = Client(api);

        var result = await client.Assets.Initialize(Media, [Logo], default, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ConflictError>().Message.ShouldContain("does not exist");
    }

    [Fact]
    public async Task A_throttled_call_carries_its_retry_after()
    {
        using var api = new StubHandler().Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30));
            return response;
        });
        using var client = Client(api);

        var result = await client.Assets.Initialize(Media, [Logo], default, TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<RateLimitError>();
        error.ShouldBeAssignableTo<TransientError>();
        error.RetryAfter.GetValueOrDefault().ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task An_empty_batch_is_refused_without_a_round_trip()
    {
        using var api = new StubHandler();
        using var client = Client(api);

        var result = await client.Assets.Initialize(Media, [], default, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>();
        api.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task More_than_a_hundred_assets_are_refused_without_a_round_trip()
    {
        using var api = new StubHandler();
        using var client = Client(api);

        var assets = Enumerable
            .Range(0, AssetsClient.MaxAssetsPerInitialize + 1)
            .Select(index => new Asset(AssetKey.From($"asset_{index}"), AssetFilename.From("logo.png")))
            .ToList();

        var result = await client.Assets.Initialize(Media, assets, default, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("100");
        api.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task More_than_fifty_assets_are_refused_by_the_calls_that_cap_at_fifty()
    {
        using var api = new StubHandler();
        using var client = Client(api);

        var assets = Enumerable
            .Range(0, 51)
            .Select(index => new Asset(AssetKey.From($"asset_{index}"), AssetFilename.From("logo.png")))
            .ToList();
        var keys = assets.Select(asset => asset.Key).ToList();

        (await client.Assets.Complete(Media, assets, TestContext.Current.CancellationToken))
            .Error.ShouldBeOfType<ValidationError>();
        (await client.Assets.RefreshUploadLinks(Media, assets, TestContext.Current.CancellationToken))
            .Error.ShouldBeOfType<ValidationError>();
        (await client.Assets.GetState(Media, keys, TestContext.Current.CancellationToken))
            .Error.ShouldBeOfType<ValidationError>();
        (await client.Assets.Delete(Media, keys, TestContext.Current.CancellationToken))
            .Error.ShouldBeOfType<ValidationError>();

        api.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Exactly_the_ceiling_is_accepted_and_sent()
    {
        const string nothing = """
            { "initialized": {}, "failed": {} }
            """;

        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, nothing)
            .Respond(HttpStatusCode.OK, """{ "completed": {}, "failed": {} }""")
            .Respond(HttpStatusCode.OK, nothing)
            .Respond(HttpStatusCode.OK, "{}")
            .Respond(HttpStatusCode.NoContent, EmptyBody);
        using var client = Client(api);

        var hundred = Batch(AssetsClient.MaxAssetsPerInitialize);
        var fifty = Batch(AssetsClient.MaxAssetsPerCompletion);
        var fiftyKeys = Batch(AssetsClient.MaxKeysPerRequest).Select(asset => asset.Key).ToList();

        // The ceiling is the last accepted value, not the first rejected one.
        (await client.Assets.Initialize(Media, hundred, default, TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
        (await client.Assets.Complete(Media, fifty, TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
        (await client.Assets.RefreshUploadLinks(Media, fifty, TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
        (await client.Assets.GetState(Media, fiftyKeys, TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();
        (await client.Assets.Delete(Media, fiftyKeys, TestContext.Current.CancellationToken))
            .IsSuccess.ShouldBeTrue();

        api.RequestCount.ShouldBe(5);

        static List<Asset> Batch(int count) =>
        [
            .. Enumerable
                .Range(0, count)
                .Select(index => new Asset(AssetKey.From($"asset_{index}"), AssetFilename.From("logo.png"))),
        ];
    }

    [Fact]
    public async Task Repeated_keys_are_refused_without_a_round_trip()
    {
        using var api = new StubHandler();
        using var client = Client(api);
        List<AssetKey> repeated = [AssetKey.From("logo"), AssetKey.From("logo")];

        (await client.Assets.GetState(Media, repeated, TestContext.Current.CancellationToken))
            .Error.ShouldBeOfType<ValidationError>();
        (await client.Assets.Delete(Media, repeated, TestContext.Current.CancellationToken))
            .Error.ShouldBeOfType<ValidationError>();

        api.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Keys_differing_only_in_case_are_a_read_the_server_accepts()
    {
        const string states = """
            { "logo": { "status": "Completed" } }
            """;

        using var api = new StubHandler().Respond(HttpStatusCode.OK, states);
        using var client = Client(api);

        // Reads and deletes compare keys exactly, the way Occtoo compares them
        // there — rejecting this pair locally would refuse a call it accepts.
        var result = await client.Assets.GetState(
            Media,
            [AssetKey.From("logo"), AssetKey.From("LOGO")],
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        api.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task Keys_that_differ_only_in_case_are_refused_the_way_occtoo_compares_them()
    {
        using var api = new StubHandler();
        using var client = Client(api);

        var result = await client.Assets.Initialize(
            Media,
            [Logo, new Asset(AssetKey.From("LOGO"), AssetFilename.From("logo.png"))],
            default,
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("distinct");
        api.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Even_a_null_batch_is_a_result_not_an_exception()
    {
        using var api = new StubHandler();
        using var client = Client(api);

        var result = await client.Assets.Initialize(
            Media, null!, default, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>();
        api.RequestCount.ShouldBe(0);
    }
}
