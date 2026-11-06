using System.Net;
using System.Text.Json;
using Occtoo.Assets;
using Occtoo.ImageConversions;
using Occtoo.Sdk.Tests.Assets;
using Shouldly;
using Xunit;
using static Occtoo.Sdk.Tests.Assets.AssetsTestSetup;
using static Occtoo.Sdk.Tests.ImageConversions.ImageConversionsTestSetup;

namespace Occtoo.Sdk.Tests.ImageConversions;

public class ImageConversionsClientTests
{
    private static readonly byte[] Bytes = "12345"u8.ToArray();

    [Fact]
    public async Task Upload_sends_the_filename_and_returns_the_id()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Created, Created());
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        var result = await client.ImageConversions.Upload(
            AssetContent.FromBytes(Bytes), "shoe.tif", TestContext.Current.CancellationToken);

        result.Value.ShouldBe(ConversionId);

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/image-conversions");

        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("filename").GetString().ShouldBe("shoe.tif");
    }

    [Fact]
    public async Task The_bytes_go_to_the_upload_url_as_a_block_blob_with_no_occtoo_credential()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Created, Created());
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        await client.ImageConversions.Upload(
            AssetContent.FromBytes(Bytes), "shoe.tif", TestContext.Current.CancellationToken);

        var request = blob.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Put);
        request.RequestUri!.ToString().ShouldBe(OriginalUrl);
        request.Header("x-ms-blob-type").ShouldBe("BlockBlob");
        request.Header("Authorization").ShouldBeNull();
        request.Header("x-api-key").ShouldBeNull();
        request.Body.ShouldBe("12345");
    }

    [Fact]
    public async Task Content_of_the_maximum_file_size_is_refused_before_the_bytes_are_sent()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Created, Created(maxFileSize: 5));
        using var blob = new StubHandler();
        using var client = Client(api, blob);

        var result = await client.ImageConversions.Upload(
            AssetContent.FromBytes(Bytes), "shoe.tif", TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Message.ShouldContain("5 bytes");
        blob.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task Content_just_under_the_maximum_file_size_is_sent()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Created, Created(maxFileSize: 6));
        using var blob = new StubHandler().RespondCreated();
        using var client = Client(api, blob);

        var result = await client.ImageConversions.Upload(
            AssetContent.FromBytes(Bytes), "shoe.tif", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        blob.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_stream_of_unknown_length_is_refused_before_any_call()
    {
        using var api = new StubHandler();
        using var blob = new StubHandler();
        using var client = Client(api, blob);
        await using var unknown = new UnseekableStream(Bytes);

        var result = await client.ImageConversions.Upload(
            AssetContent.FromStream(unknown), "shoe.tif", TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("FromStream(stream, length)");
        api.RequestCount.ShouldBe(0);
        blob.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_400_on_create_is_a_validation_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.BadRequest, BadFilenameBody);
        using var blob = new StubHandler();
        using var client = Client(api, blob);

        var result = await client.ImageConversions.Upload(
            AssetContent.FromBytes(Bytes), "shoe.tif", TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ValidationError>().Message.ShouldContain("filename is not valid");
        blob.RequestCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_403_from_storage_is_an_authentication_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Created, Created());
        using var blob = new StubHandler()
            .RespondStorageFailure(HttpStatusCode.Forbidden, "AuthenticationFailed");
        using var client = Client(api, blob);

        var result = await client.ImageConversions.Upload(
            AssetContent.FromBytes(Bytes), "shoe.tif", TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<AuthenticationError>();
        error.Message.ShouldContain("shoe.tif");
        error.Message.ShouldContain("AuthenticationFailed");
    }

    [Fact]
    public async Task Convert_writes_enum_member_names_and_leaves_out_unset_optional_fields()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, ConvertedBody);
        using var client = Client(api);

        await client.ImageConversions.Convert(ConversionId, Thumbnail, TestContext.Current.CancellationToken);

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe($"https://api.occtoo.com/v1/image-conversions/{Id}/convert");

        using var body = JsonDocument.Parse(request.Body!);
        var parameters = body.RootElement.GetProperty("parameters");
        parameters.GetProperty("mode").GetString().ShouldBe("Fit");
        parameters.GetProperty("targetSize").GetInt32().ShouldBe(400);
        parameters.GetProperty("aspectRatio").GetString().ShouldBe("1:1");
        parameters.GetProperty("outputFormat").GetString().ShouldBe("Webp");
        parameters.GetProperty("quality").GetInt32().ShouldBe(80);
        parameters.TryGetProperty("paddingPercent", out _).ShouldBeFalse();
        parameters.TryGetProperty("background", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Convert_sends_padding_and_background_when_set()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, ConvertedBody);
        using var client = Client(api);

        await client.ImageConversions.Convert(ConversionId, Padded, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(api.Requests.Single().Body!);
        var parameters = body.RootElement.GetProperty("parameters");
        parameters.GetProperty("mode").GetString().ShouldBe("FillBackground");
        parameters.GetProperty("outputFormat").GetString().ShouldBe("Jpg");
        parameters.GetProperty("paddingPercent").GetInt32().ShouldBe(5);
        parameters.GetProperty("aspectRatio").GetString().ShouldBe("4:3");
        parameters.GetProperty("background").GetString().ShouldBe("FFFFFF");
    }

    [Fact]
    public async Task The_convert_response_maps_to_a_converted_image()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, ConvertedBody);
        using var client = Client(api);

        var result = await client.ImageConversions.Convert(
            ConversionId, Thumbnail, TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new ConvertedImage(
            new Uri(DownloadUrl),
            DateTimeOffset.Parse(DownloadExpiry, null),
            "shoe.webp",
            "image/webp",
            81234));
    }

    [Fact]
    public async Task A_404_is_a_not_found_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.NotFound, NotFoundBody);
        using var client = Client(api);

        var result = await client.ImageConversions.Convert(
            ConversionId, Thumbnail, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NotFoundError>();
    }

    [Fact]
    public async Task A_rejected_target_size_lands_in_the_failures()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.BadRequest, TargetSizeRejectedBody);
        using var client = Client(api);

        var result = await client.ImageConversions.Convert(
            ConversionId, Thumbnail with { TargetSize = 9000 }, TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Failures["Parameters.TargetSize"].Single().ShouldContain("8000");
    }

    [Fact]
    public async Task A_504_is_a_server_error_and_is_not_resent()
    {
        using var api = new StubHandler().RespondAlways(HttpStatusCode.GatewayTimeout, TimedOutBody);
        using var client = RetryingClient(api);

        var result = await client.ImageConversions.Convert(
            ConversionId, Thumbnail, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ServerError>();
        api.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_download_link_that_is_not_an_absolute_url_is_an_unexpected_error()
    {
        const string relative = """
            {
              "url": "shoe.webp",
              "expiresAt": "2099-01-01T00:00:00+00:00",
              "filename": "shoe.webp",
              "contentType": "image/webp",
              "size": 81234
            }
            """;

        using var api = new StubHandler().Respond(HttpStatusCode.OK, relative);
        using var client = Client(api);

        var result = await client.ImageConversions.Convert(
            ConversionId, Thumbnail, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Theory]
    [InlineData("1x1")]
    [InlineData("0:1")]
    [InlineData("01:1")]
    [InlineData("101:1")]
    public void An_aspect_ratio_rejects_anything_but_two_sides_from_1_to_100(string value) =>
        ImageAspectRatio.TryFrom(value, out _).ShouldBeFalse();

    [Theory]
    [InlineData("1:1")]
    [InlineData("100:3")]
    public void An_aspect_ratio_accepts_two_sides_from_1_to_100(string value) =>
        ImageAspectRatio.TryFrom(value, out _).ShouldBeTrue();

    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("FFF")]
    [InlineData("GGGGGG")]
    public void A_background_rejects_anything_but_six_hex_digits(string value) =>
        ImageBackground.TryFrom(value, out _).ShouldBeFalse();

    [Theory]
    [InlineData("ffffff")]
    [InlineData("00AAFF")]
    public void A_background_accepts_six_hex_digits_in_either_case(string value) =>
        ImageBackground.TryFrom(value, out _).ShouldBeTrue();
}
