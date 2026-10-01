using System.Net;
using Occtoo.Assets;
using Occtoo.Authentication;
using Occtoo.Http;
using Occtoo.Sources;

namespace Occtoo.Sdk.Tests.Assets;

/// <summary>
/// The scripted responses and the two-transport wiring every asset test works
/// from: one stub for the Occtoo API, one for blob storage, neither of which
/// reaches a real environment.
/// </summary>
internal static class AssetsTestSetup
{
    internal static readonly SourceId Media = SourceId.From("media");

    internal const string UploadUrl = "https://tenant.blob.core.windows.net/media/logo.png?sig=abc";

    internal const string RefreshedUploadUrl = "https://tenant.blob.core.windows.net/media/logo.png?sig=def";

    /// <summary>
    /// Link expiry sits far in the future throughout: the SDK re-signs a link
    /// it can see is expired, so a date that quietly passes would turn every
    /// upload test into a refresh test.
    /// </summary>
    internal const string LinkExpiry = "2099-01-01T00:00:00+00:00";

    internal const string InitializedBody = $$"""
        {
          "initialized": {
            "logo": { "uploadUrl": "{{UploadUrl}}", "expiresAt": "{{LinkExpiry}}" }
          },
          "failed": {}
        }
        """;

    internal const string RefreshedBody = $$"""
        {
          "initialized": {
            "logo": { "uploadUrl": "{{RefreshedUploadUrl}}", "expiresAt": "{{LinkExpiry}}" }
          },
          "failed": {}
        }
        """;

    /// <summary>A link Occtoo signed long enough ago that the SDK can see it is dead.</summary>
    internal const string ExpiredBody = $$"""
        {
          "initialized": {
            "logo": { "uploadUrl": "{{UploadUrl}}", "expiresAt": "2020-01-01T00:00:00+00:00" }
          },
          "failed": {}
        }
        """;

    internal const string CompletedBody = """
        {
          "completed": {
            "logo": {
              "filename": "logo.png",
              "mimeType": "image/png",
              "size": 5,
              "publicUrl": "https://cdn.occtoo.com/media/logo.png",
              "width": 64,
              "height": 32
            }
          },
          "failed": {}
        }
        """;

    internal const string EmptyBody = "";

    /// <summary>What a rejected call comes back as: RFC 9457 problem details.</summary>
    internal const string RejectedBody = """
        {
          "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          "title": "Bad Request",
          "status": 400,
          "detail": "Asset uploads can only target Media data sources"
        }
        """;

    internal static Asset Logo => new(AssetKey.From("logo"), AssetFilename.From("logo.png"));

    /// <summary>An initialize response signing one link per key.</summary>
    internal static string Initialized(IReadOnlyCollection<string> keys)
    {
        var entries = keys.Select(key =>
            $$"""
              "{{key}}": { "uploadUrl": "{{UploadUrl}}", "expiresAt": "{{LinkExpiry}}" }
              """);

        return $$"""
            { "initialized": { {{string.Join(",", entries)}} }, "failed": {} }
            """;
    }

    /// <summary>A completion response creating one file per key.</summary>
    internal static string Completed(IReadOnlyCollection<string> keys)
    {
        var entries = keys.Select(key =>
            $$"""
              "{{key}}": {
                "filename": "logo.png",
                "mimeType": "image/png",
                "size": 5,
                "publicUrl": "https://cdn.occtoo.com/media/logo.png"
              }
              """);

        return $$"""
            { "completed": { {{string.Join(",", entries)}} }, "failed": {} }
            """;
    }

    internal static OcctooClient Client(StubHandler api) => Client(api, new StubHandler());

    internal static OcctooClient Client(StubHandler api, StubHandler blob)
    {
        var credential = OcctooCredential.ApiKey(ApiKey.From("key-1"));

        // Authentication sits in front of the API transport and nowhere near
        // the upload one — which is the point of the second client.
        return new OcctooClient(
            new HttpClient(new OcctooAuthenticationHandler(credential) { InnerHandler = api }),
            new OcctooClientOptions
            {
                Credential = credential,
                UploadHttpClient = new HttpClient(blob),
            });
    }

    /// <summary>A blob storage reply to a <c>Put Blob</c>.</summary>
    internal static StubHandler RespondCreated(this StubHandler handler) =>
        handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.Created));

    /// <summary>A blob storage failure, in storage's own vocabulary.</summary>
    internal static StubHandler RespondStorageFailure(
        this StubHandler handler,
        HttpStatusCode status,
        string errorCode) =>
        handler.Respond(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent($"<Error><Code>{errorCode}</Code></Error>"),
            };
            response.Headers.TryAddWithoutValidation("x-ms-error-code", errorCode);
            response.Headers.TryAddWithoutValidation("x-ms-request-id", "storage-request-1");
            return response;
        });
}

/// <summary>
/// A stream that knows neither its length nor its position — a pipe, a
/// decompressor, a network read. Content over one of these can be read once and
/// never again, which is what makes a retry impossible.
/// </summary>
internal sealed class UnseekableStream(byte[] bytes) : Stream
{
    private readonly MemoryStream _inner = new(bytes);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();

        base.Dispose(disposing);
    }
}

/// <summary>
/// An <see cref="IProgress{T}"/> the SDK invokes on the thread that produced
/// the report, so the recorded order is the order things happened —
/// <see cref="Progress{T}"/> would queue them to the thread pool and reorder.
/// </summary>
internal sealed class RecordingProgress : IProgress<AssetProgress>
{
    private readonly Lock _gate = new();

    public List<AssetProgress> Reports { get; } = [];

    public void Report(AssetProgress value)
    {
        lock (_gate)
            Reports.Add(value);
    }

    public IReadOnlyList<AssetProgress> For(string key) =>
        [.. Reports.Where(report => report.Key.Value == key)];
}
