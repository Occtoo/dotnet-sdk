using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CSharpFunctionalExtensions;
using Occtoo.Http.Internal;

namespace Occtoo.Assets.Internal;

/// <summary>
/// A failed attempt to upload an asset's bytes to blob storage.
/// <see cref="Status"/> is the HTTP status blob storage responded with, or null if
/// there was no response at all. <see cref="AssetsClient.Upload"/> checks it to spot an
/// expired link worth re-signing via <see cref="AssetsClient.RefreshUploadLinks"/> and
/// retrying.
/// </summary>
internal sealed record BlobFailure(OcctooError Error, HttpStatusCode? Status);

/// <summary>
/// The one request that moves an asset's bytes: a single <c>Put Blob</c>
/// against the signed URL Occtoo handed out.
/// </summary>
/// <remarks>
/// The URL carries its own authorization, so this runs on the client's
/// unauthenticated upload transport — an Occtoo token must never reach a
/// storage account. The upload is one request and is not resumable.
/// </remarks>
internal static class BlobTransfer
{
    /// <summary>Put Blob is only authorized for a block blob, and only says so through this header.</summary>
    internal const string BlobTypeHeader = "x-ms-blob-type";

    internal const string BlockBlob = "BlockBlob";

    private const string ErrorCodeHeader = "x-ms-error-code";
    private const string RequestIdHeader = "x-ms-request-id";
    private const int MaxBodySnippet = 512;

    internal static async Task<Result<AssetTransfer, BlobFailure>> Put(
        HttpClient httpClient,
        AssetUploadLink link,
        AssetContent content,
        long length,
        AssetTransferOptions options,
        CancellationToken cancellationToken)
    {
        var opened = await content.Open(cancellationToken).ConfigureAwait(false);

        return await opened.Match(
            stream => Send(httpClient, link, stream, length, options, cancellationToken),
            error => Task.FromResult(
                Result.Failure<AssetTransfer, BlobFailure>(new BlobFailure(error, null))))
            .ConfigureAwait(false);
    }

    private static async Task<Result<AssetTransfer, BlobFailure>> Send(
        HttpClient httpClient,
        AssetUploadLink link,
        Stream opened,
        long length,
        AssetTransferOptions options,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        // The stream is disposed with the request: what Open returned is the
        // transfer's to close, and a consumer's own stream came back wrapped.
        var stream = options.Progress is { } progress
            ? new ProgressStream(opened, length, transferred => Report(progress, link, length, transferred))
            : opened;

        using var request = new HttpRequestMessage(HttpMethod.Put, link.Url);
        request.Headers.TryAddWithoutValidation(BlobTypeHeader, BlockBlob);
        request.Content = new StreamContent(stream);
        // The stored mime type is Occtoo's reading of the bytes, not this
        // header — but a Content-Length is mandatory, because storage rejects a
        // chunked body.
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = length;

        var sent = await OcctooTransport
            .Send(httpClient, options.Timeout, request, cancellationToken, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        return await sent.Match(
            async Task<Result<AssetTransfer, BlobFailure>> (response) =>
            {
                using (response)
                {
                    if (response.IsSuccessStatusCode)
                        return new AssetTransfer(link.Key, length, Stopwatch.GetElapsedTime(started));

                    var body = await ReadSnippet(response, cancellationToken).ConfigureAwait(false);
                    return new BlobFailure(Classify(response, link, body), response.StatusCode);
                }
            },
            error => Task.FromResult(
                Result.Failure<AssetTransfer, BlobFailure>(new BlobFailure(Unreachable(error, link), null))))
            .ConfigureAwait(false);
    }

    private static void Report(IProgress<AssetProgress> progress, AssetUploadLink link, long total, long transferred) =>
        progress.Report(new AssetProgress(
            link.Key,
            link.Filename,
            AssetUploadStage.Transferring,
            transferred,
            Maybe.From(total),
            Maybe<OcctooError>.None));

    /// <summary>
    /// A failure with no response behind it. The transport names Occtoo in its
    /// message, which is the wrong service here, so the asset and the storage
    /// host are named instead.
    /// </summary>
    private static OcctooError Unreachable(OcctooError error, AssetUploadLink link) => error switch
    {
        NetworkError => new NetworkError(
            $"Uploading '{link.Filename.Value}' to blob storage failed: {link.Url.Host} could not be reached."),
        TimeoutError => new TimeoutError(
            $"Uploading '{link.Filename.Value}' to blob storage did not finish within the transfer timeout."),
        _ => error,
    };

    /// <summary>
    /// Blob storage answers in its own vocabulary: the error code and request id
    /// are what a storage support ticket starts from, and they are headers, so
    /// no XML parser is needed to find them.
    /// </summary>
    private static OcctooError Classify(HttpResponseMessage response, AssetUploadLink link, string body)
    {
        var prefix = $"Uploading '{link.Filename.Value}' to blob storage failed";
        var detail = Describe(response, body);

        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden => new AuthenticationError(
                $"{prefix}: the upload link has expired or is not valid. {detail}"),
            HttpStatusCode.NotFound => new NotFoundError(
                $"{prefix}: the blob it points at no longer exists. {detail}"),
            HttpStatusCode.BadRequest => new UnexpectedError(
                $"{prefix}: storage rejected the request the SDK built. {detail}",
                (int)response.StatusCode),
            HttpStatusCode.Conflict => new ConflictError($"{prefix}: {detail}"),
            HttpStatusCode.RequestEntityTooLarge => new ValidationError(
                $"{prefix}: the content is larger than storage accepts in one request. {detail}"),
            HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => new RateLimitError(
                $"{prefix}: the storage account is busy. {detail}",
                OcctooApiErrors.RetryAfter(response)),
            >= HttpStatusCode.InternalServerError => new ServerError(
                $"{prefix}: {detail}",
                (int)response.StatusCode),
            _ => new UnexpectedError($"{prefix}: {detail}", (int)response.StatusCode),
        };
    }

    private static string Describe(HttpResponseMessage response, string body)
    {
        var description = $"Storage responded {(int)response.StatusCode}";

        if (Header(response, ErrorCodeHeader) is { Length: > 0 } code)
            description += $", {code}";

        if (Header(response, RequestIdHeader) is { Length: > 0 } requestId)
            description += $" (x-ms-request-id: {requestId})";

        return body is { Length: > 0 }
            ? $"{description}. {body}"
            : $"{description}.";
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.FirstOrDefault()
            : null;

    /// <summary>
    /// As much of storage's own error body as goes in a message, and not a byte
    /// more.
    /// </summary>
    /// <remarks>
    /// This is the one response the SDK reads straight off the network rather
    /// than from a buffer: it comes from a host that is not Occtoo, and it can
    /// be happening on several transfers at once. A proxy in front of the
    /// storage account answering a 403 with a page of HTML is not worth
    /// reading, so the read stops at the snippet.
    /// </remarks>
    private static async Task<string> ReadSnippet(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var scratch = ArrayPool<byte>.Shared.Rent(MaxBodySnippet);

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var read = 0;

            while (read < MaxBodySnippet)
            {
                var count = await stream
                    .ReadAsync(scratch.AsMemory(read, MaxBodySnippet - read), cancellationToken)
                    .ConfigureAwait(false);

                if (count == 0)
                    break;

                read += count;
            }

            // Storage answers XML in UTF-8; a snippet can cut a multi-byte
            // character in half, costing one replacement character in a diagnostic string.
            return Encoding.UTF8.GetString(scratch, 0, read);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return "";
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }
}
