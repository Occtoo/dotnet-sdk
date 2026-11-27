using System.Net;
using System.Text.Json;
using CSharpFunctionalExtensions;
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
        using var api = new StubHandler().Respond(HttpStatusCode.Accepted, QueuedTaskBody);
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
        using var api = new StubHandler().Respond(HttpStatusCode.Accepted, QueuedTaskBody);
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
    public async Task Convert_returns_the_queued_task_id()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Accepted, QueuedTaskBody);
        using var client = Client(api);

        var result = await client.ImageConversions.Convert(
            ConversionId, Thumbnail, TestContext.Current.CancellationToken);

        result.Value.ShouldBe(TaskId);
    }

    [Fact]
    public async Task A_202_without_a_task_id_is_an_unexpected_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Accepted, """{ "status": "Queued" }""");
        using var client = Client(api);

        var result = await client.ImageConversions.Convert(
            ConversionId, Thumbnail, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
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
    public async Task Convert_with_a_preset_posts_only_the_preset_id()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Accepted, QueuedTaskBody);
        using var client = Client(api);

        await client.ImageConversions.Convert(ConversionId, PresetIdValue, TestContext.Current.CancellationToken);

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe($"https://api.occtoo.com/v1/image-conversions/{Id}/convert");
        request.Body.ShouldBe($$"""{"preset":"{{PresetIdValue}}"}""");
    }

    [Fact]
    public async Task Convert_with_a_preset_returns_the_queued_task_id()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Accepted, QueuedTaskBody);
        using var client = Client(api);

        var result = await client.ImageConversions.Convert(
            ConversionId, PresetId, TestContext.Current.CancellationToken);

        result.Value.ShouldBe(TaskId);
    }

    [Fact]
    public async Task A_404_on_Convert_with_a_preset_is_a_not_found_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.NotFound, NotFoundBody);
        using var client = Client(api);

        var result = await client.ImageConversions.Convert(
            ConversionId, PresetId, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NotFoundError>();
    }

    [Fact]
    public async Task GetTask_gets_the_task_under_its_conversion()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, QueuedTaskBody);
        using var client = Client(api);

        await client.ImageConversions.GetTask(ConversionId, TaskId, TestContext.Current.CancellationToken);

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri!.ToString().ShouldBe($"https://api.occtoo.com/v1/image-conversions/{Id}/tasks/{TaskIdValue}");
    }

    [Fact]
    public async Task A_completed_task_maps_to_its_converted_image()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, CompletedTaskBody);
        using var client = Client(api);

        var result = await client.ImageConversions.GetTask(ConversionId, TaskId, TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new ConversionTask(
            TaskId,
            ConversionTaskStatus.Completed,
            new ConvertedImage(
                new Uri(DownloadUrl),
                DateTimeOffset.Parse(DownloadExpiry, null),
                "shoe.webp",
                "image/webp",
                81234),
            Maybe<string>.None));
    }

    [Fact]
    public async Task A_failed_task_carries_the_error_and_no_image()
    {
        using var api = new StubHandler().Respond(
            HttpStatusCode.OK,
            TaskBody("Failed", error: "\"The uploaded file can't be converted.\""));
        using var client = Client(api);

        var result = await client.ImageConversions.GetTask(ConversionId, TaskId, TestContext.Current.CancellationToken);

        var task = result.Value;
        task.Status.GetValueOrThrow().ShouldBe(ConversionTaskStatus.Failed);
        task.Error.GetValueOrThrow().ShouldBe("The uploaded file can't be converted.");
        task.Image.HasNoValue.ShouldBeTrue();
    }

    [Fact]
    public async Task A_queued_task_has_neither_an_image_nor_an_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, QueuedTaskBody);
        using var client = Client(api);

        var result = await client.ImageConversions.GetTask(ConversionId, TaskId, TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new ConversionTask(
            TaskId,
            ConversionTaskStatus.Queued,
            Maybe<ConvertedImage>.None,
            Maybe<string>.None));
    }

    [Fact]
    public async Task A_task_with_an_unknown_status_has_no_status()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, TaskBody("Paused"));
        using var client = Client(api);

        var result = await client.ImageConversions.GetTask(ConversionId, TaskId, TestContext.Current.CancellationToken);

        result.Value.Status.HasNoValue.ShouldBeTrue();
    }

    [Fact]
    public async Task A_completed_task_whose_download_link_is_not_an_absolute_url_is_an_unexpected_error()
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

        using var api = new StubHandler().Respond(HttpStatusCode.OK, TaskBody("Completed", result: relative));
        using var client = Client(api);

        var result = await client.ImageConversions.GetTask(ConversionId, TaskId, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Fact]
    public async Task A_completed_task_without_a_result_is_an_unexpected_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, TaskBody("Completed"));
        using var client = Client(api);

        var result = await client.ImageConversions.GetTask(ConversionId, TaskId, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Fact]
    public async Task A_404_on_GetTask_is_a_not_found_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.NotFound, NotFoundBody);
        using var client = Client(api);

        var result = await client.ImageConversions.GetTask(ConversionId, TaskId, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NotFoundError>();
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

    [Fact]
    public async Task ListPresets_gets_the_first_page_of_presets()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, PresetPage(after: null));
        using var client = Client(api);

        await client.ImageConversions.ListPresets(cancellationToken: TestContext.Current.CancellationToken);

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/conversion-presets?limit=50");
    }

    [Fact]
    public async Task ListPresets_sends_the_page_request_in_the_query()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, PresetPage(after: null));
        using var client = Client(api);

        await client.ImageConversions.ListPresets(
            new PageRequest { After = PageCursor.From("web-large"), Limit = 10, IncludeTotal = true },
            TestContext.Current.CancellationToken);

        api.Requests.Single().RequestUri!.Query.ShouldBe("?after=web-large&limit=10&includeTotalCount=true");
    }

    [Fact]
    public async Task A_preset_page_with_a_next_cursor_has_more()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, PresetPage(after: "c1"));
        using var client = Client(api);

        var result = await client.ImageConversions.ListPresets(cancellationToken: TestContext.Current.CancellationToken);

        var page = result.Value;
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(PresetId);
        page.Next.GetValueOrThrow().Value.ShouldBe("c1");
        page.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task A_preset_page_without_a_next_cursor_is_the_last()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, PresetPage(after: null));
        using var client = Client(api);

        var result = await client.ImageConversions.ListPresets(cancellationToken: TestContext.Current.CancellationToken);

        var page = result.Value;
        page.Items.ShouldHaveSingleItem();
        page.Next.HasNoValue.ShouldBeTrue();
        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task GetPreset_gets_the_preset_by_id()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, PresetBody());
        using var client = Client(api);

        await client.ImageConversions.GetPreset(PresetId, TestContext.Current.CancellationToken);

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.RequestUri!.ToString().ShouldBe($"https://api.occtoo.com/v1/conversion-presets/{PresetIdValue}");
    }

    [Fact]
    public async Task A_preset_response_maps_to_a_conversion_preset()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, PresetBody());
        using var client = Client(api);

        var result = await client.ImageConversions.GetPreset(PresetId, TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new ConversionPreset(
            PresetId,
            "Web large",
            Padded,
            2,
            DateTimeOffset.Parse(PresetCreatedAt, null),
            DateTimeOffset.Parse(PresetUpdatedAt, null)));
    }

    [Fact]
    public async Task A_preset_with_an_unknown_mode_has_no_parameters()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, PresetBody(mode: "Stretch"));
        using var client = Client(api);

        var result = await client.ImageConversions.GetPreset(PresetId, TestContext.Current.CancellationToken);

        result.Value.Parameters.HasNoValue.ShouldBeTrue();
    }

    [Fact]
    public async Task A_preset_with_a_malformed_aspect_ratio_is_an_unexpected_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, PresetBody(aspectRatio: "4x3"));
        using var client = Client(api);

        var result = await client.ImageConversions.GetPreset(PresetId, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<UnexpectedError>();
    }

    [Fact]
    public async Task A_404_on_GetPreset_is_a_not_found_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.NotFound, NotFoundBody);
        using var client = Client(api);

        var result = await client.ImageConversions.GetPreset(PresetId, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NotFoundError>();
    }

    [Fact]
    public async Task CreatePreset_posts_the_id_display_name_and_parameters()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Created, PresetBody());
        using var client = Client(api);

        await client.ImageConversions.CreatePreset(
            PresetId, "Web large", Padded, TestContext.Current.CancellationToken);

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://api.occtoo.com/v1/conversion-presets");
        request.Body.ShouldBe(
            """{"id":"web-large","displayName":"Web large","parameters":{"mode":"FillBackground","targetSize":2000,"aspectRatio":"4:3","paddingPercent":5,"background":"FFFFFF","outputFormat":"Jpg","quality":90}}""");
    }

    [Fact]
    public async Task CreatePreset_returns_the_created_preset()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Created, PresetBody());
        using var client = Client(api);

        var result = await client.ImageConversions.CreatePreset(
            PresetId, "Web large", Padded, TestContext.Current.CancellationToken);

        result.Value.Parameters.GetValueOrThrow().ShouldBe(Padded);
    }

    [Fact]
    public async Task A_409_on_CreatePreset_is_a_conflict_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Conflict, PresetConflictBody);
        using var client = Client(api);

        var result = await client.ImageConversions.CreatePreset(
            PresetId, "Web large", Padded, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ConflictError>();
    }

    [Fact]
    public async Task A_rejected_display_name_lands_in_the_failures()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.BadRequest, DisplayNameRejectedBody);
        using var client = Client(api);

        var result = await client.ImageConversions.CreatePreset(
            PresetId, "Web large", Padded, TestContext.Current.CancellationToken);

        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Failures[DisplayNameKey].Single().ShouldContain("between 2 and 50");
    }

    [Fact]
    public async Task UpdatePreset_puts_the_display_name_and_parameters_without_the_id()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.OK, PresetBody());
        using var client = Client(api);

        await client.ImageConversions.UpdatePreset(
            PresetId, "Web thumbnail", Thumbnail, TestContext.Current.CancellationToken);

        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Put);
        request.RequestUri!.ToString().ShouldBe($"https://api.occtoo.com/v1/conversion-presets/{PresetIdValue}");
        request.Body.ShouldBe(
            """{"displayName":"Web thumbnail","parameters":{"mode":"Fit","targetSize":400,"aspectRatio":"1:1","outputFormat":"Webp","quality":80}}""");
    }

    [Fact]
    public async Task A_404_on_UpdatePreset_is_a_not_found_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.NotFound, NotFoundBody);
        using var client = Client(api);

        var result = await client.ImageConversions.UpdatePreset(
            PresetId, "Web large", Padded, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<NotFoundError>();
    }

    [Fact]
    public async Task DeletePreset_deletes_the_preset_by_id()
    {
        using var api = new StubHandler().Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = Client(api);

        var result = await client.ImageConversions.DeletePreset(PresetId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var request = api.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Delete);
        request.RequestUri!.ToString().ShouldBe($"https://api.occtoo.com/v1/conversion-presets/{PresetIdValue}");
    }

    [Fact]
    public async Task A_409_on_DeletePreset_is_a_conflict_error()
    {
        using var api = new StubHandler().Respond(HttpStatusCode.Conflict, PresetConflictBody);
        using var client = Client(api);

        var result = await client.ImageConversions.DeletePreset(PresetId, TestContext.Current.CancellationToken);

        result.Error.ShouldBeOfType<ConflictError>();
    }

    [Theory]
    [InlineData("Web-large")]
    [InlineData("1-large")]
    [InlineData("web large")]
    [InlineData("web.large")]
    [InlineData("a2345678901234567890123456789012345678901234567890123456789012345")]
    public void A_preset_id_rejects_anything_but_a_lower_case_slug_of_at_most_64_characters(string value) =>
        ConversionPresetId.TryFrom(value, out _).ShouldBeFalse();

    [Theory]
    [InlineData("w")]
    [InlineData("web_large-2")]
    [InlineData("a234567890123456789012345678901234567890123456789012345678901234")]
    public void A_preset_id_accepts_a_lower_case_slug_of_at_most_64_characters(string value) =>
        ConversionPresetId.TryFrom(value, out _).ShouldBeTrue();

    [Theory]
    [InlineData(" Web large")]
    [InlineData("Web large ")]
    [InlineData("W")]
    [InlineData("Web großes Bild")]
    [InlineData("Web\tlarge")]
    [InlineData("A12345678901234567890123456789012345678901234567890")]
    public void A_preset_display_name_rejects_padded_short_long_or_non_printable_ascii_names(string value) =>
        ConversionPresetDisplayName.TryFrom(value, out _).ShouldBeFalse();

    [Theory]
    [InlineData("Wb")]
    [InlineData("Web large (4:3) ~ #1")]
    [InlineData("A1234567890123456789012345678901234567890123456789")]
    public void A_preset_display_name_accepts_2_to_50_printable_ascii_characters(string value) =>
        ConversionPresetDisplayName.TryFrom(value, out _).ShouldBeTrue();
}
