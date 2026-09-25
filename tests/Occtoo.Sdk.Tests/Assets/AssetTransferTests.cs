using System.Net;
using System.Text;
using Occtoo.Assets;
using Shouldly;
using Xunit;
using static Occtoo.Sdk.Tests.Assets.AssetsTestSetup;

namespace Occtoo.Sdk.Tests.Assets;

public class AssetTransferTests
{
    private static readonly byte[] Bytes = "12345"u8.ToArray();

    private static AssetUploadLink Link => new(
        AssetKey.From("logo"),
        AssetFilename.From("logo.png"),
        new Uri(UploadUrl),
        DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public async Task The_bytes_go_to_the_signed_url_as_a_block_blob()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        var result = await client.Assets.Transfer(
            Link, AssetContent.FromBytes(Bytes), null, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Bytes.ShouldBe(5);
        result.Value.Key.ShouldBe(AssetKey.From("logo"));

        var request = blob.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Put);
        request.RequestUri!.ToString().ShouldBe(UploadUrl);
        request.Header("x-ms-blob-type").ShouldBe("BlockBlob");
        request.Body.ShouldBe("12345");
        api.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_upload_carries_a_length_and_no_occtoo_credential()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        await client.Assets.Transfer(
            Link, AssetContent.FromBytes(Bytes), null, TestContext.Current.CancellationToken);

        var request = blob.Requests.Single();

        // Without Content-Length the request would be chunked, which Put Blob rejects.
        request.ContentHeader("Content-Length").ShouldBe("5");
        request.ContentHeader("Content-Type").ShouldBe("application/octet-stream");

        // The SAS carries its own authorization; an Occtoo token here would be
        // a token handed to a third party.
        request.Header("Authorization").ShouldBeNull();
        request.Header("x-api-key").ShouldBeNull();
    }

    [Fact]
    public async Task A_file_is_streamed_from_disk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"occtoo-sdk-{Guid.NewGuid():n}.txt");
        await File.WriteAllTextAsync(path, "12345", TestContext.Current.CancellationToken);

        try
        {
            using var api = new StubHandler();
            using var blob = new StubHandler().RespondCreated();
            using var client = Client(api, blob);

            var result = await client.Assets.Transfer(
                Link, AssetContent.FromFile(path), null, TestContext.Current.CancellationToken);

            result.Value.Bytes.ShouldBe(5);
            blob.Requests.Single().Body.ShouldBe("12345");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_missing_file_is_refused_before_anything_is_sent()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler();
        using var client = Client(api, blob);

        var result = await client.Assets.Transfer(
            Link,
            AssetContent.FromFile(Path.Combine(Path.GetTempPath(), "occtoo-sdk-does-not-exist.txt")),
            null,
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("does not exist");
        blob.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_stream_that_cannot_report_its_length_is_refused_at_the_call_site()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler();
        using var client = Client(api, blob);
        await using var unknown = new UnseekableStream(Bytes);

        var result = await client.Assets.Transfer(
            Link, AssetContent.FromStream(unknown), null, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("FromStream(stream, length)");
        blob.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_stream_whose_length_the_caller_states_is_sent()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);
        await using var unknown = new UnseekableStream(Bytes);

        var result = await client.Assets.Transfer(
            Link, AssetContent.FromStream(unknown, 5), null, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        blob.Requests.Single().Body.ShouldBe("12345");
    }

    [Fact]
    public async Task Content_larger_than_one_upload_can_carry_is_refused_locally()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler();
        using var client = Client(api, blob);
        await using var huge = new UnseekableStream(Bytes);

        var result = await client.Assets.Transfer(
            Link,
            AssetContent.FromStream(huge, AssetsClient.MaxContentLength + 1),
            null,
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>()
            .Message.ShouldContain(AssetsClient.MaxContentLength.ToString(null as IFormatProvider));
        blob.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_storage_failure_names_the_asset_and_storages_own_identifiers()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.InternalServerError, "InternalError");
        using var client = Client(api, blob);

        var result = await client.Assets.Transfer(
            Link, AssetContent.FromBytes(Bytes), null, TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<ServerError>();
        error.Message.ShouldContain("logo.png");
        error.Message.ShouldContain("InternalError");
        error.Message.ShouldContain("storage-request-1");
    }

    [Fact]
    public async Task An_expired_link_is_an_authentication_failure()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.Forbidden, "AuthenticationFailed");
        using var client = Client(api, blob);

        var result = await client.Assets.Transfer(
            Link, AssetContent.FromBytes(Bytes), null, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<AuthenticationError>().Message.ShouldContain("expired");
    }

    [Fact]
    public async Task A_request_storage_rejected_is_the_sdks_mistake_not_the_callers()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.BadRequest, "MissingRequiredHeader");
        using var client = Client(api, blob);

        var result = await client.Assets.Transfer(
            Link, AssetContent.FromBytes(Bytes), null, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>().Message.ShouldContain("MissingRequiredHeader");
    }

    [Fact]
    public async Task An_unreachable_storage_account_is_transient()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler().Respond(_ => throw new HttpRequestException("down"));
        using var client = Client(api, blob);

        var result = await client.Assets.Transfer(
            Link, AssetContent.FromBytes(Bytes), null, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NetworkError>().ShouldBeAssignableTo<TransientError>();
    }

    [Fact]
    public async Task A_transfer_that_outlives_its_timeout_is_a_timeout_error()
    {
        using var api = new StubHandler();
        // Held open until the SDK's own timeout cancels it — a stub that threw
        // the cancellation itself would only be testing the stub.
        using var blob = new StubHandler().Respond(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        using var client = Client(api, blob);

        var result = await client.Assets.Transfer(
            Link,
            AssetContent.FromBytes(Bytes),
            new AssetTransferOptions { Timeout = TimeSpan.FromMilliseconds(50) },
            TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<TimeoutError>();
        blob.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_callers_cancellation_stays_an_exception()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await client.Assets.Transfer(Link, AssetContent.FromBytes(Bytes), null, cancelled.Token));
    }

}
