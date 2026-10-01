using System.Net;
using Occtoo.Assets;
using Shouldly;
using Xunit;
using static Occtoo.Sdk.Tests.Assets.AssetsTestSetup;

namespace Occtoo.Sdk.Tests.Assets;

public class AssetProgressTests
{
    private static AssetUpload Logo =>
        AssetUpload.FromBytes(AssetKey.From("logo"), AssetFilename.From("logo.png"), "12345"u8.ToArray());

    [Fact]
    public async Task One_asset_reports_every_stage_it_passes_through()
    {
        var progress = new RecordingProgress();
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        await client.Assets.Upload(
            Media,
            [Logo],
            new AssetUploadOptions { MaxConcurrentTransfers = 1, Progress = progress },
            TestContext.Current.CancellationToken);

        var stages = progress.For("logo").Select(report => report.Stage).ToList();

        stages[0].ShouldBe(AssetUploadStage.Initializing);
        stages[1].ShouldBe(AssetUploadStage.Transferring);
        stages[^2].ShouldBe(AssetUploadStage.Completing);
        stages[^1].ShouldBe(AssetUploadStage.Completed);
    }

    [Fact]
    public async Task Byte_counts_never_go_backwards_and_end_at_the_full_length()
    {
        var progress = new RecordingProgress();
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        await client.Assets.Upload(
            Media,
            [Logo],
            new AssetUploadOptions { MaxConcurrentTransfers = 1, Progress = progress },
            TestContext.Current.CancellationToken);

        var transferring = progress.For("logo")
            .Where(report => report.Stage == AssetUploadStage.Transferring)
            .Select(report => report.BytesTransferred)
            .ToList();

        transferring.ShouldBe(transferring.Order().ToList());
        transferring[^1].ShouldBe(5);
        progress.For("logo").ShouldAllBe(report => report.TotalBytes.GetValueOrDefault() == 5);
    }

    [Fact]
    public async Task A_failure_ends_the_asset_with_one_report_carrying_the_stage_and_the_error()
    {
        var progress = new RecordingProgress();
        using var api = new StubHandler().Respond(HttpStatusCode.OK, InitializedBody);
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.NotFound, "ContainerNotFound");
        using var client = Client(api, blob);

        await client.Assets.Upload(
            Media,
            [Logo],
            new AssetUploadOptions { MaxConcurrentTransfers = 1, Progress = progress },
            TestContext.Current.CancellationToken);

        var failures = progress.For("logo").Where(report => report.Failure.HasValue).ToList();

        failures.Count.ShouldBe(1);
        failures[0].Stage.ShouldBe(AssetUploadStage.Transferring);
        failures[0].Failure.GetValueOrDefault().ShouldBeOfType<NotFoundError>();
        progress.For("logo")[^1].ShouldBe(failures[0]);
    }

    [Fact]
    public async Task Several_assets_at_once_each_report_their_own_stages_in_order()
    {
        var progress = new RecordingProgress();
        var assets = Enumerable
            .Range(0, 8)
            .Select(index => AssetUpload.FromBytes(
                AssetKey.From($"asset_{index}"), AssetFilename.From("logo.png"), "12345"u8.ToArray()))
            .ToList();
        var keys = assets.Select(asset => asset.Key.Value).ToList();

        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, Initialized(keys))
            .Respond(HttpStatusCode.OK, Completed(keys));
        using var blob = new StubHandler().RespondAlways(HttpStatusCode.Created, "");
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media,
            assets,
            new AssetUploadOptions { MaxConcurrentTransfers = 4, Progress = progress },
            TestContext.Current.CancellationToken);

        result.Value.AllCompleted.ShouldBeTrue();

        // Nothing is promised about the order between assets; each asset's own
        // reports still arrive in the order they happened.
        foreach (var key in keys)
        {
            var stages = progress.For(key).Select(report => report.Stage).ToList();
            stages[0].ShouldBe(AssetUploadStage.Initializing);
            stages[^1].ShouldBe(AssetUploadStage.Completed);
            stages.ShouldBe(stages.Order().ToList());
        }
    }

    [Fact]
    public async Task A_transfer_sent_again_reports_its_bytes_again_and_ends_at_the_full_length()
    {
        var progress = new RecordingProgress();
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.InternalServerError, "InternalError")
            .RespondCreated();
        using var client = Client(api, blob);

        await client.Assets.Upload(
            Media,
            [Logo],
            new AssetUploadOptions { MaxConcurrentTransfers = 1, Progress = progress },
            TestContext.Current.CancellationToken);

        var transferring = progress.For("logo")
            .Where(report => report.Stage == AssetUploadStage.Transferring)
            .Select(report => report.BytesTransferred)
            .ToList();

        // Zero is announced once, for the first attempt; the second attempt
        // reads the content again and reports its way back up.
        transferring.Count(bytes => bytes == 0).ShouldBe(1);
        transferring[^1].ShouldBe(5);
        blob.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task An_asset_that_moved_every_byte_and_then_failed_reports_what_it_moved()
    {
        const string refused = """
            { "completed": {}, "failed": { "logo": "Asset is not initialized" } }
            """;

        var progress = new RecordingProgress();
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, refused);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        await client.Assets.Upload(
            Media,
            [Logo],
            new AssetUploadOptions { MaxConcurrentTransfers = 1, Progress = progress },
            TestContext.Current.CancellationToken);

        var terminal = progress.For("logo")[^1];
        terminal.Stage.ShouldBe(AssetUploadStage.Completing);
        terminal.Failure.HasValue.ShouldBeTrue();
        terminal.BytesTransferred.ShouldBe(5);
    }

    [Fact]
    public async Task Reporting_nothing_changes_nothing()
    {
        using var api = new StubHandler()
            .Respond(HttpStatusCode.OK, InitializedBody)
            .Respond(HttpStatusCode.OK, CompletedBody);
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        var result = await client.Assets.Upload(
            Media,
            [Logo],
            new AssetUploadOptions { MaxConcurrentTransfers = 1 },
            TestContext.Current.CancellationToken);

        result.Value.AllCompleted.ShouldBeTrue();
    }
}
