using System.Diagnostics;
using CSharpFunctionalExtensions;
using Occtoo.Assets;
using Occtoo.Assets.Internal;
using Occtoo.Http.Internal;
using Occtoo.ImageConversions.Internal;
using Occtoo.Telemetry;

namespace Occtoo.ImageConversions;

/// <summary>
/// Upload an image once with <see cref="Upload"/>, then convert it as often as
/// you like, either with your own parameters:
/// <see cref="Convert(ImageConversionId, ImageConversionParameters, CancellationToken)"/>
/// or with one of the tenant's presets:
/// <see cref="Convert(ImageConversionId, ConversionPresetId, CancellationToken)"/>.
/// This client also manages the tenant's conversion presets.
/// </summary>
/// <remarks>
/// Occtoo deletes the original a day after the upload. Uploading, converting
/// and changing presets need
/// <see cref="Authentication.OcctooScopes.WriteMedia"/>; reading presets needs
/// <see cref="Authentication.OcctooScopes.ReadMedia"/>.
/// </remarks>
public sealed class ImageConversionsClient
{
    private const string UploadOperation = "upload image";
    private const string RequestOperation = "request image conversion";
    private const string ConvertOperation = "convert image";
    private const string ListPresetsOperation = "list conversion presets";
    private const string GetPresetOperation = "get conversion preset";
    private const string CreatePresetOperation = "create conversion preset";
    private const string UpdatePresetOperation = "update conversion preset";
    private const string DeletePresetOperation = "delete conversion preset";

    private const string IdTag = "occtoo.image_conversions.id";
    private const string BytesTag = "occtoo.image_conversions.bytes";
    private const string PresetTag = "occtoo.image_conversions.preset";

    private static readonly Uri ImageConversionsUri = new("v1/image-conversions", UriKind.Relative);
    private static readonly Uri PresetsUri = new("v1/conversion-presets", UriKind.Relative);

    private readonly HttpClient _httpClient;
    private readonly Lazy<HttpClient> _uploadHttpClient;
    private readonly TimeSpan _requestTimeout;

    internal ImageConversionsClient(
        HttpClient httpClient,
        Lazy<HttpClient> uploadHttpClient,
        TimeSpan requestTimeout)
    {
        _httpClient = httpClient;
        _uploadHttpClient = uploadHttpClient;
        _requestTimeout = requestTimeout;
    }

    /// <summary>
    /// Uploads an image to convert.
    /// </summary>
    /// <param name="content">The image's bytes.</param>
    /// <param name="filename">The image's filename, such as <c>shoe.tif</c>.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <returns>
    /// The id to pass to
    /// <see cref="Convert(ImageConversionId, ImageConversionParameters, CancellationToken)"/>
    /// or <see cref="Convert(ImageConversionId, ConversionPresetId, CancellationToken)"/>,
    /// or why the upload failed:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ValidationError"/>: the content's length is unknown, the
    /// content isn't smaller than the maximum Occtoo accepts, or Occtoo refuses
    /// the filename.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused or lacks the scope.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed.
    /// </description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Nothing is resent. After a <see cref="TransientError"/>, upload again.
    /// </remarks>
    public async Task<Result<ImageConversionId, OcctooError>> Upload(
        AssetContent content,
        AssetFilename filename,
        CancellationToken cancellationToken = default)
    {
        if (content is null)
            return new ValidationError("Content is required.");

        using var activity = OcctooTelemetry.Source.StartActivity(UploadOperation, ActivityKind.Client);

        return await content.ResolveLength()
            .Ensure(
                length => length >= 0,
                length => new ValidationError(
                    $"The content states a length of {length} bytes. A length can't be negative."))
            .Bind(length => RequestUpload(filename, cancellationToken)
                .Map(target => (Target: target, Length: length)))
            .Tap(upload => activity?.SetTag(IdTag, upload.Target.Id.Value.ToString("D")))
            .Ensure(
                upload => upload.Length < upload.Target.MaxFileSize,
                upload => new ValidationError(
                    $"The content is {upload.Length} bytes. An image to convert must be smaller than "
                    + $"{upload.Target.MaxFileSize} bytes."))
            .Check(upload => BlobTransfer.Put(
                _uploadHttpClient.Value,
                upload.Target.Url,
                filename.Value,
                content,
                upload.Length,
                Timeout.InfiniteTimeSpan,
                progress: null,
                cancellationToken))
            .Tap(upload => activity?.SetTag(BytesTag, upload.Length))
            .Map(upload => upload.Target.Id)
            .TapError(error => OcctooTelemetry.Fail(activity, error))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Converts an uploaded image and returns a link to download the result.
    /// </summary>
    /// <param name="id">The id <see cref="Upload"/> returned.</param>
    /// <param name="parameters">How to convert the image.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The converted image, or why the conversion failed:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ValidationError"/>: a parameter Occtoo refuses, keyed in
    /// <see cref="ValidationError.Failures"/> by its name, such as
    /// <c>Parameters.TargetSize</c>. Without keys: the original is too large or
    /// isn't an image Occtoo can convert.
    /// </description></item>
    /// <item><description>
    /// <see cref="NotFoundError"/>: the original is unknown, not uploaded yet,
    /// or already deleted.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused or lacks the scope.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed, including a <c>504</c> when
    /// the conversion ran over 25 seconds.
    /// </description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Nothing is resent after a <c>5xx</c> or a timeout.
    /// </remarks>
    public async Task<Result<ConvertedImage, OcctooError>> Convert(
        ImageConversionId id,
        ImageConversionParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters is null)
            return new ValidationError("Conversion parameters are required.");

        return await SendConvert(
                id,
                new ConvertImageDto { Parameters = ToParametersDto(parameters) },
                [new(IdTag, id.Value.ToString("D"))],
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Converts an uploaded image with one of the tenant's conversion presets
    /// and returns a link to download the result.
    /// </summary>
    /// <param name="id">The id <see cref="Upload"/> returned.</param>
    /// <param name="preset">The id of the preset to convert with.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The converted image, or why the conversion failed:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ValidationError"/>: the original is too large or isn't an
    /// image Occtoo can convert.
    /// </description></item>
    /// <item><description>
    /// <see cref="NotFoundError"/>: the original is unknown, not uploaded yet,
    /// already deleted, or no preset has the id. The error doesn't say which.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused or lacks the scope.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed, including a <c>504</c> when
    /// the conversion ran over 25 seconds.
    /// </description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Nothing is resent after a <c>5xx</c> or a timeout.
    /// </remarks>
    public Task<Result<ConvertedImage, OcctooError>> Convert(
        ImageConversionId id,
        ConversionPresetId preset,
        CancellationToken cancellationToken = default) =>
        SendConvert(
            id,
            new ConvertImageDto { Preset = preset.Value },
            [new(IdTag, id.Value.ToString("D")), new(PresetTag, preset.Value)],
            cancellationToken);

    /// <summary>
    /// Reads one page of the tenant's conversion presets, ordered by id.
    /// </summary>
    /// <param name="page">Where to read from and how much; absent reads the first 50.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The page, or why the read failed:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ValidationError"/>: the limit isn't from 1 to 200, or Occtoo
    /// refuses the cursor, keyed in <see cref="ValidationError.Failures"/> as
    /// <c>after</c>.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused or lacks the scope.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed.
    /// </description></item>
    /// </list>
    /// </returns>
    public Task<Result<Page<ConversionPreset>, OcctooError>> ListPresets(
        PageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        page ??= new PageRequest();
        var uri = new QueryString("v1/conversion-presets")
            .Add(page)
            .ToUri();

        return Pages.Validate(page)
            .Bind(() => OcctooTransport.Send(
                _httpClient,
                _requestTimeout,
                OcctooTransport.Request(HttpMethod.Get, uri),
                ListPresetsOperation,
                ImageConversionsJsonContext.Default.ForwardPageDtoConversionPresetDto,
                cancellationToken,
                [new("occtoo.page.limit", page.Limit)]))
            .MapResponse(presets => Pages.ToPage(
                presets.Items ?? throw new MalformedResponseException("'items' is missing."),
                presets.After,
                presets.TotalCount,
                ToPreset));
    }

    /// <summary>
    /// Reads one conversion preset.
    /// </summary>
    /// <param name="id">The preset's id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The preset, or why the read failed:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="NotFoundError"/>: no preset has the id.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused or lacks the scope.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed.
    /// </description></item>
    /// </list>
    /// </returns>
    public Task<Result<ConversionPreset, OcctooError>> GetPreset(
        ConversionPresetId id,
        CancellationToken cancellationToken = default) =>
        OcctooTransport
            .Send(
                _httpClient,
                _requestTimeout,
                OcctooTransport.Request(HttpMethod.Get, PresetUri(id)),
                GetPresetOperation,
                ImageConversionsJsonContext.Default.ConversionPresetDto,
                cancellationToken,
                [new(PresetTag, id.Value)])
            .MapResponse(ToPreset);

    /// <summary>
    /// Creates a conversion preset.
    /// </summary>
    /// <param name="id">The new preset's id. It can't change later.</param>
    /// <param name="displayName">The new preset's display name.</param>
    /// <param name="parameters">How the preset converts an image.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The created preset, or why the create failed:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ValidationError"/>: a value Occtoo refuses, keyed in
    /// <see cref="ValidationError.Failures"/> as <c>Id</c>,
    /// <c>DisplayName</c> or a parameter's name, such as
    /// <c>Parameters.TargetSize</c>.
    /// </description></item>
    /// <item><description>
    /// <see cref="ConflictError"/>: a preset with the id already exists.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused or lacks the scope.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed.
    /// </description></item>
    /// </list>
    /// </returns>
    public async Task<Result<ConversionPreset, OcctooError>> CreatePreset(
        ConversionPresetId id,
        ConversionPresetDisplayName displayName,
        ImageConversionParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters is null)
            return new ValidationError("Conversion parameters are required.");

        return await OcctooTransport
            .Send(
                _httpClient,
                _requestTimeout,
                OcctooTransport.Request(
                    HttpMethod.Post,
                    PresetsUri,
                    new(id.Value, displayName.Value, ToParametersDto(parameters)),
                    ImageConversionsJsonContext.Default.CreateConversionPresetDto),
                CreatePresetOperation,
                ImageConversionsJsonContext.Default.ConversionPresetDto,
                cancellationToken,
                [new(PresetTag, id.Value)])
            .MapResponse(ToPreset)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces a conversion preset's display name and parameters.
    /// </summary>
    /// <param name="id">The preset's id.</param>
    /// <param name="displayName">The preset's new display name.</param>
    /// <param name="parameters">How the preset converts an image from now on.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The updated preset, or why the update failed:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ValidationError"/>: a value Occtoo refuses, keyed in
    /// <see cref="ValidationError.Failures"/> as <c>DisplayName</c> or a
    /// parameter's name, such as <c>Parameters.TargetSize</c>.
    /// </description></item>
    /// <item><description>
    /// <see cref="NotFoundError"/>: no preset has the id.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused or lacks the scope.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed.
    /// </description></item>
    /// </list>
    /// </returns>
    public async Task<Result<ConversionPreset, OcctooError>> UpdatePreset(
        ConversionPresetId id,
        ConversionPresetDisplayName displayName,
        ImageConversionParameters parameters,
        CancellationToken cancellationToken = default)
    {
        if (parameters is null)
            return new ValidationError("Conversion parameters are required.");

        return await OcctooTransport
            .Send(
                _httpClient,
                _requestTimeout,
                OcctooTransport.Request(
                    HttpMethod.Put,
                    PresetUri(id),
                    new(displayName.Value, ToParametersDto(parameters)),
                    ImageConversionsJsonContext.Default.UpdateConversionPresetDto),
                UpdatePresetOperation,
                ImageConversionsJsonContext.Default.ConversionPresetDto,
                cancellationToken,
                [new(PresetTag, id.Value)])
            .MapResponse(ToPreset)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a conversion preset. Deleting a preset that doesn't exist
    /// succeeds.
    /// </summary>
    /// <param name="id">The preset's id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// Success, or why the delete failed:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ConflictError"/>: a card definition still uses the preset.
    /// </description></item>
    /// <item><description>
    /// <see cref="NotFoundError"/>: a concurrent change removed something the
    /// preset uses.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused or lacks the scope.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed.
    /// </description></item>
    /// </list>
    /// </returns>
    public Task<UnitResult<OcctooError>> DeletePreset(
        ConversionPresetId id,
        CancellationToken cancellationToken = default) =>
        OcctooTransport.Send(
            _httpClient,
            _requestTimeout,
            OcctooTransport.Request(HttpMethod.Delete, PresetUri(id)),
            DeletePresetOperation,
            cancellationToken,
            [new(PresetTag, id.Value)]);

    private Task<Result<UploadTarget, OcctooError>> RequestUpload(
        AssetFilename filename,
        CancellationToken cancellationToken) =>
        OcctooTransport
            .Send(
                _httpClient,
                _requestTimeout,
                OcctooTransport.Request(
                    HttpMethod.Post,
                    ImageConversionsUri,
                    new(filename.Value),
                    ImageConversionsJsonContext.Default.RequestImageConversionDto),
                RequestOperation,
                ImageConversionsJsonContext.Default.ImageConversionCreatedDto,
                cancellationToken,
                responseTags: created => [new(IdTag, created.Id.ToString("D"))])
            .Bind(ToUploadTarget);

    private Task<Result<ConvertedImage, OcctooError>> SendConvert(
        ImageConversionId id,
        ConvertImageDto body,
        IReadOnlyList<KeyValuePair<string, object?>> tags,
        CancellationToken cancellationToken) =>
        OcctooTransport
            .Send(
                _httpClient,
                _requestTimeout,
                OcctooTransport.Request(
                    HttpMethod.Post,
                    new($"v1/image-conversions/{id.Value:D}/convert", UriKind.Relative),
                    body,
                    ImageConversionsJsonContext.Default.ConvertImageDto),
                ConvertOperation,
                ImageConversionsJsonContext.Default.ConvertedImageDto,
                cancellationToken,
                tags)
            .Bind(ToConvertedImage);

    private static Result<UploadTarget, OcctooError> ToUploadTarget(
        ImageConversionCreatedDto created)
    {
        if (!ImageConversionId.TryFrom(created.Id, out var id))
            return new UnexpectedError("Occtoo returned an image conversion without an id.");

        if (created.UploadUrl is not { Length: > 0 } url || !Uri.TryCreate(url, UriKind.Absolute, out var location))
            return new UnexpectedError($"Occtoo returned an upload link for image conversion '{id}' that isn't a URL.");

        if (created.MaxFileSize is not > 0)
            return new UnexpectedError($"Occtoo returned image conversion '{id}' without a maximum file size.");

        return new UploadTarget(id, location, created.MaxFileSize.Value);
    }

    private static Result<ConvertedImage, OcctooError> ToConvertedImage(ConvertedImageDto converted) =>
        converted.Url is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var location)
            ? new ConvertedImage(location, converted.ExpiresAt, converted.Filename, converted.ContentType, converted.Size)
            : new UnexpectedError("Occtoo returned a converted image whose download link isn't a URL.");

    private static ImageConversionParametersDto ToParametersDto(ImageConversionParameters parameters) => new(
        parameters.Mode,
        parameters.TargetSize,
        parameters.AspectRatio.Value,
        parameters.PaddingPercent.HasValue ? parameters.PaddingPercent.Value : null,
        parameters.Background.HasValue ? parameters.Background.Value.Value : null,
        parameters.OutputFormat,
        parameters.Quality);

    private static ConversionPreset ToPreset(ConversionPresetDto preset)
    {
        var parameters = preset.Parameters
            ?? throw new MalformedResponseException($"Conversion preset '{preset.Id}' has no parameters.");
        var aspectRatio = ImageAspectRatio.From(parameters.AspectRatio);
        var background = parameters.Background is { } color
            ? Maybe.From(ImageBackground.From(color))
            : Maybe<ImageBackground>.None;

        return new ConversionPreset(
            ConversionPresetId.From(preset.Id),
            preset.DisplayName,
            Enums.Read<ImageConversionMode>(parameters.Mode)
                .Bind(mode => Enums.Read<ImageOutputFormat>(parameters.OutputFormat)
                    .Map(outputFormat => new ImageConversionParameters(
                        mode,
                        parameters.TargetSize,
                        aspectRatio,
                        outputFormat,
                        parameters.Quality)
                    {
                        PaddingPercent = parameters.PaddingPercent is { } padding ? Maybe.From(padding) : Maybe<int>.None,
                        Background = background,
                    })),
            preset.Version,
            preset.CreatedAt,
            preset.UpdatedAt);
    }

    private static Uri PresetUri(ConversionPresetId id) => new($"v1/conversion-presets/{id.Value}", UriKind.Relative);

    private sealed record UploadTarget(ImageConversionId Id, Uri Url, long MaxFileSize);
}
