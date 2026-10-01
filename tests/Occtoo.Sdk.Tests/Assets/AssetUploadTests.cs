using System.Net;
using System.Text.Json;
using Occtoo.Assets;
using Shouldly;
using Xunit;
using static Occtoo.Sdk.Tests.Assets.AssetsTestSetup;

namespace Occtoo.Sdk.Tests.Assets;

public class AssetUploadTests
{
    // The transfers run in parallel, and the stub records every request it is
    // given into one list. One at a time keeps the recording honest.
    private static AssetUploadOptions Sequential => new() { MaxConcurrentTransfers = 1 };

    private static AssetUpload Logo(string key = "logo") =>
        AssetUpload.FromBytes(AssetKey.From(key), AssetFilename.From("logo.png"), "12345"u8.ToArray());

    [Fact]
    public async Task An_upload_initializes_then_sends_the_bytes_then_completes()
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AllCompleted.ShouldBeTrue();

        var outcome = result.Value.Outcomes.Single();
        outcome.Reached.ShouldBe(AssetUploadStage.Completed);
        outcome.Outcome.Value.MimeType.ShouldBe("image/png");
        outcome.Outcome.Value.PublicUrl.ShouldBe(new Uri("https://cdn.occtoo.com/media/logo.png"));

        api.Requests[0].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media");
        blob.Requests.Single().RequestUri!.ToString().ShouldBe(UploadUrl);
        api.Requests[1].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media/complete");
    }

    [Fact]
    public async Task Completion_uses_the_filename_the_asset_was_initialized_under()
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        await client.Assets.Upload(Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        Filenames(api.Requests[0].Body!).ShouldBe(Filenames(api.Requests[1].Body!));

        static IReadOnlyList<string> Filenames(string body)
        {
            using var json = JsonDocument.Parse(body);
            return
            [
                .. json.RootElement.GetProperty("assets").EnumerateArray()
                    .Select(asset => asset.GetProperty("filename").GetString()!),
            ];
        }
    }

    [Fact]
    public async Task An_asset_occtoo_refused_at_initialize_is_never_sent()
    {
        const string refused = """
            { "initialized": {}, "failed": { "logo": "Asset already completed" } }
            """;

        using var api = new StubHandler().Respond(HttpStatusCode.OK, refused);
        using var blob = new StubHandler();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        var outcome = result.Value.Outcomes.Single();
        outcome.Reached.ShouldBe(AssetUploadStage.Initializing);
        outcome.Outcome.Error.ShouldBeOfType<AssetRejectedError>().Message.ShouldBe("Asset already completed");

        blob.RequestCount.ShouldBe(0);
        api.RequestCount.ShouldBe(1);
        result.Value.AllCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task An_asset_whose_bytes_never_arrive_is_never_completed()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, InitializedBody);
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.NotFound, "ContainerNotFound");
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        var outcome = result.Value.Outcomes.Single();
        outcome.Reached.ShouldBe(AssetUploadStage.Transferring);
        outcome.Outcome.Error.ShouldBeOfType<NotFoundError>();

        // Initialize only: nothing was completed.
        api.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_asset_occtoo_refused_at_completion_says_where_it_stopped()
    {
        const string refused = """
            { "completed": {}, "failed": { "logo": "Asset is not initialized" } }
            """;

        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, refused);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        var outcome = result.Value.Outcomes.Single();
        outcome.Reached.ShouldBe(AssetUploadStage.Completing);
        outcome.Outcome.Error.ShouldBeOfType<AssetRejectedError>().Message.ShouldBe("Asset is not initialized");
    }

    [Fact]
    public async Task A_transient_storage_failure_is_sent_again_from_the_start_of_the_content()
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.InternalServerError, "InternalError")
            .RespondCreated();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        result.Value.AllCompleted.ShouldBeTrue();
        blob.RequestCount.ShouldBe(2);
        blob.Requests[1].Body.ShouldBe("12345");
    }

    [Fact]
    public async Task An_expired_link_is_re_signed_once_and_the_bytes_sent_to_the_fresh_url()
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, RefreshedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.Forbidden, "AuthenticationFailed")
            .RespondCreated();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        result.Value.AllCompleted.ShouldBeTrue();
        api.Requests[1].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media/uploadLinks");
        blob.Requests[0].RequestUri!.ToString().ShouldBe(UploadUrl);
        blob.Requests[1].RequestUri!.ToString().ShouldBe(RefreshedUploadUrl);
    }

    [Fact]
    public async Task A_link_that_is_already_expired_is_re_signed_before_the_bytes_are_sent()
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, ExpiredBody)
            .Respond(HttpStatusCode.OK, RefreshedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        result.Value.AllCompleted.ShouldBeTrue();

        // One PUT, to the fresh link: pushing a file at a link the SDK can
        // already see is expired only earns a 403.
        api.Requests[1].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media/uploadLinks");
        blob.Requests.Single().RequestUri!.ToString().ShouldBe(RefreshedUploadUrl);
    }

    [Fact]
    public async Task A_re_sign_that_fails_still_sends_the_bytes()
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, ExpiredBody)
            .Respond(_ => Throttled())
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        // The expiry is the local clock's opinion. It tried to re-sign, could
        // not, and sent the bytes to the old link anyway — which storage
        // accepted, because the clock was wrong rather than the link dead.
        api.Requests[1].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media/uploadLinks");
        blob.Requests.Single().RequestUri!.ToString().ShouldBe(UploadUrl);
        result.Value.AllCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_re_sign_that_fails_leaves_the_one_storage_answers_with()
    {
        var progress = new RecordingProgress();
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, ExpiredBody)
            .Respond(_ => Throttled())
            .Respond(HttpStatusCode.OK, RefreshedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.Forbidden, "AuthenticationFailed")
            .RespondCreated();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media,
            [Logo()],
            new AssetUploadOptions { MaxConcurrentTransfers = 1, Progress = progress },
            TestContext.Current.CancellationToken);

        // The clock was right, the pre-emptive re-sign failed, and the 403 the
        // link earned still had a re-sign behind it.
        result.Value.AllCompleted.ShouldBeTrue();
        api.Requests[2].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media/uploadLinks");
        blob.Requests[1].RequestUri!.ToString().ShouldBe(RefreshedUploadUrl);

        // A re-signed link is not a new transfer: the asset announced its start
        // once, not once per link.
        progress.For("logo")
            .Count(report => report is { Stage: AssetUploadStage.Transferring, BytesTransferred: 0 })
            .ShouldBe(1);
    }

    private static HttpResponseMessage Throttled()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30));
        return response;
    }

    [Fact]
    public async Task Content_that_cannot_be_read_twice_is_sent_once_and_only_once()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, InitializedBody);
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.InternalServerError, "InternalError")
            .RespondCreated();
        using var client = Client(api, blob);
        await using var unseekable = new UnseekableStream("12345"u8.ToArray());

        var result = await client.Assets.Upload(
            Media,
            [new AssetUpload(AssetKey.From("logo"), AssetFilename.From("logo.png"),
                AssetContent.FromStream(unseekable, 5))],
            Sequential,
            TestContext.Current.CancellationToken);

        // The failure is transient and an attempt is left, but the bytes are
        // gone: a second PUT would send an empty body.
        blob.RequestCount.ShouldBe(1);
        result.Value.Outcomes.Single().Reached.ShouldBe(AssetUploadStage.Transferring);
        result.Value.AllCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_that_cannot_start_still_ends_every_asset_it_was_given()
    {
        var progress = new RecordingProgress();
        using var api = new StubHandler().Respond(HttpStatusCode.Conflict, RejectedBody);
        using var blob = new StubHandler();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media,
            [Logo("logo"), Logo("note_1")],
            new AssetUploadOptions { MaxConcurrentTransfers = 1, Progress = progress },
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();

        // A view rendered from progress must not be left showing both files
        // stuck on "initializing".
        foreach (var key in new[] { "logo", "note_1" })
        {
            var last = progress.For(key)[^1];
            last.Stage.ShouldBe(AssetUploadStage.Initializing);
            last.Failure.GetValueOrDefault().ShouldBeOfType<ConflictError>();
        }
    }

    [Fact]
    public async Task Cancelling_a_run_surfaces_as_an_exception_not_a_failed_result()
    {
        using var cancelled = new CancellationTokenSource();
        using var api = new StubHandler().Respond(HttpStatusCode.OK, InitializedBody);
        using var blob = new StubHandler().Respond(async (_, token) =>
        {
            await cancelled.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        using var client = Client(api, blob);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await client.Assets.Upload(Media, [Logo()], Sequential, cancelled.Token));
    }

    [Fact]
    public async Task Sixty_assets_initialize_once_and_complete_twice()
    {
        var assets = Enumerable.Range(0, 60).Select(index => Logo($"asset_{index}")).ToList();
        var keys = assets.Select(asset => asset.Key.Value).ToList();

        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, Initialized(keys))
            .Respond(HttpStatusCode.OK, Completed([.. keys.Take(50)]))
            .Respond(HttpStatusCode.OK, Completed([.. keys.Skip(50)]));
        using var blob = new StubHandler().RespondAlways(HttpStatusCode.Created, "");
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, assets, Sequential, TestContext.Current.CancellationToken);

        result.Value.AllCompleted.ShouldBeTrue();
        api.RequestCount.ShouldBe(3);
        api.Requests[0].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media");
        api.Requests[1].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media/complete");
        api.Requests[2].RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/assets/media/complete");
        blob.RequestCount.ShouldBe(60);
    }

    [Fact]
    public async Task The_report_keeps_the_order_the_assets_were_passed_in()
    {
        var assets = Enumerable.Range(0, 5).Select(index => Logo($"asset_{index}")).ToList();
        var keys = assets.Select(asset => asset.Key.Value).ToList();

        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, Initialized(keys))
            .Respond(HttpStatusCode.OK, Completed(keys));
        using var blob = new StubHandler().RespondAlways(HttpStatusCode.Created, "");
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, assets, new AssetUploadOptions { MaxConcurrentTransfers = 4 },
            TestContext.Current.CancellationToken);

        result.Value.Outcomes.Select(outcome => outcome.Key.Value).ShouldBe(keys);
    }

    [Fact]
    public async Task A_run_that_cannot_start_fails_as_a_whole()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Conflict, RejectedBody);
        using var blob = new StubHandler();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media, [Logo()], Sequential, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<ConflictError>();
        blob.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_run_is_planned_before_anything_is_sent()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler();
        using var client = Client(api, blob);

        var missing = AssetUpload.FromFile(
            AssetKey.From("logo"), Path.Combine(Path.GetTempPath(), "occtoo-sdk-does-not-exist.txt"));

        var result = await client.Assets.Upload(
            Media, [missing], Sequential, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("logo");
        api.RequestCount.ShouldBe(0);
        blob.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Nonsense_options_are_refused_before_anything_is_sent()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media,
            [Logo()],
            new AssetUploadOptions { MaxConcurrentTransfers = 0 },
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>();
        api.RequestCount.ShouldBe(0);
    }

}
