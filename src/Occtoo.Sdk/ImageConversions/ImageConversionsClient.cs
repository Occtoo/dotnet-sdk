using System.Diagnostics;
using CSharpFunctionalExtensions;
using Occtoo.Assets;
using Occtoo.Assets.Internal;
using Occtoo.Http.Internal;
using Occtoo.ImageConversions.Internal;
using Occtoo.Telemetry;

namespace Occtoo.ImageConversions;

/// <summary>
/// The image conversion calls, <c>/v1/image-conversions</c>. Reached through
/// <see cref="OcctooClient.ImageConversions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Upload an image once with <see cref="Upload"/>, then call
/// <see cref="Convert"/> as often as you like. Each call converts the original
/// again and returns a fresh download link. Nothing is stored in the tenant's
/// media. Occtoo deletes the original a day after the upload.
/// </para>
/// <para>
/// The credential needs <see cref="Authentication.OcctooScopes.WriteMedia"/>.
/// </para>
/// </remarks>
public sealed class ImageConversionsClient
{
    private const string UploadOperation = "upload image";
    private const string RequestOperation = "request image conversion";
    private const string ConvertOperation = "convert image";

    private const string IdTag = "occtoo.image_conversions.id";
    private const string BytesTag = "occtoo.image_conversions.bytes";

    private static readonly Uri ImageConversionsUri = new("v1/image-conversions", UriKind.Relative);

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
    /// Uploads an image to convert. Occtoo signs an upload link that lasts
    /// 1 hour, and the bytes go straight to blob storage.
    /// </summary>
    /// <param name="content">The image's bytes.</param>
    /// <param name="filename">The image's filename, such as <c>shoe.tif</c>.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <returns>
    /// The id to pass to <see cref="Convert"/>, or why the upload failed:
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="ValidationError"/>: the content's length is unknown, or the
    /// content is as large as the maximum Occtoo accepts or larger (the message
    /// names both sizes). Neither sends any bytes. Also a filename Occtoo
    /// refuses.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused, or lacks
    /// <see cref="Authentication.OcctooScopes.WriteMedia"/>.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed.
    /// </description></item>
    /// </list>
    /// A failed transfer to blob storage comes back as an asset transfer's
    /// does: the message carries storage's error code and request id, and an
    /// expired upload link is an <see cref="AuthenticationError"/>.
    /// </returns>
    /// <remarks>
    /// The transfer is one request and isn't retried. After a
    /// <see cref="TransientError"/>, upload again. The SDK doesn't resend the
    /// request for the upload link after a <c>5xx</c> or a timeout either.
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
    /// <see cref="ValidationError"/>: a parameter breaks a rule, with
    /// <see cref="ValidationError.Failures"/> keyed <c>Parameters.TargetSize</c>,
    /// <c>Parameters.PaddingPercent</c>, <c>Parameters.Quality</c>,
    /// <c>Parameters.Mode</c> or <c>Parameters.OutputFormat</c>. Without keys:
    /// an original that is too large or isn't an image Occtoo can convert.
    /// </description></item>
    /// <item><description>
    /// <see cref="NotFoundError"/>: no original under <paramref name="id"/>:
    /// unknown, its bytes not uploaded yet, or already deleted.
    /// </description></item>
    /// <item><description>
    /// <see cref="AuthenticationError"/> or <see cref="ForbiddenError"/>: the
    /// credential is refused, or lacks
    /// <see cref="Authentication.OcctooScopes.WriteMedia"/>.
    /// </description></item>
    /// <item><description>
    /// <see cref="ServerError"/>: Occtoo failed, including a <c>504</c> when
    /// the conversion ran over 25 seconds.
    /// </description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Every call converts the original again and returns a fresh link that
    /// lasts 1 hour. The SDK doesn't resend this call after a <c>5xx</c> or a
    /// timeout.
    /// </remarks>
    public async Task<Result<ConvertedImage, OcctooError>> Convert(
        ImageConversionId id,
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
                    new Uri($"v1/image-conversions/{id.Value:D}/convert", UriKind.Relative),
                    new ConvertImageDto(new ImageConversionParametersDto(
                        parameters.Mode,
                        parameters.TargetSize,
                        parameters.AspectRatio.Value,
                        parameters.PaddingPercent.HasValue ? parameters.PaddingPercent.Value : null,
                        parameters.Background.HasValue ? parameters.Background.Value.Value : null,
                        parameters.OutputFormat,
                        parameters.Quality)),
                    ImageConversionsJsonContext.Default.ConvertImageDto),
                ConvertOperation,
                ImageConversionsJsonContext.Default.ConvertedImageDto,
                cancellationToken,
                [new(IdTag, id.Value.ToString("D"))])
            .Bind(ToConvertedImage)
            .ConfigureAwait(false);
    }

    private Task<Result<(ImageConversionId Id, Uri Url, long MaxFileSize), OcctooError>> RequestUpload(
        AssetFilename filename,
        CancellationToken cancellationToken) =>
        OcctooTransport
            .Send(
                _httpClient,
                _requestTimeout,
                OcctooTransport.Request(
                    HttpMethod.Post,
                    ImageConversionsUri,
                    new RequestImageConversionDto(filename.Value),
                    ImageConversionsJsonContext.Default.RequestImageConversionDto),
                RequestOperation,
                ImageConversionsJsonContext.Default.ImageConversionCreatedDto,
                cancellationToken,
                responseTags: created => [new(IdTag, created.Id.ToString("D"))])
            .Bind(ToUploadTarget);

    private static Result<(ImageConversionId Id, Uri Url, long MaxFileSize), OcctooError> ToUploadTarget(
        ImageConversionCreatedDto created)
    {
        if (!ImageConversionId.TryFrom(created.Id, out var id))
            return new UnexpectedError("Occtoo returned an image conversion without an id.");

        if (created.UploadUrl is not { Length: > 0 } url || !Uri.TryCreate(url, UriKind.Absolute, out var location))
            return new UnexpectedError($"Occtoo returned an upload link for image conversion '{id}' that isn't a URL.");

        if (created.MaxFileSize is not > 0)
            return new UnexpectedError($"Occtoo returned image conversion '{id}' without a maximum file size.");

        return (id, location, created.MaxFileSize.Value);
    }

    private static Result<ConvertedImage, OcctooError> ToConvertedImage(ConvertedImageDto converted) =>
        converted.Url is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var location)
            ? new ConvertedImage(location, converted.ExpiresAt, converted.Filename ?? "", converted.ContentType ?? "", converted.Size)
            : new UnexpectedError("Occtoo returned a converted image whose download link isn't a URL.");
}
