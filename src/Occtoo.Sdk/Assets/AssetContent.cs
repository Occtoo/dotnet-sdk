using System.Runtime.InteropServices;
using CSharpFunctionalExtensions;
using Occtoo.Assets.Internal;

namespace Occtoo.Assets;

/// <summary>
/// Where an asset's bytes come from.
/// </summary>
/// <remarks>
/// <para>
/// The length has to be known before the transfer starts: Azure Blob Storage
/// rejects a chunked upload, so the SDK sends <c>Content-Length</c> and never
/// buffers the content to discover it. A file, a buffer, a seekable stream and
/// the stated-length overloads all satisfy that; a non-seekable stream with no
/// stated length fails the upload before any bytes move, with a
/// <see cref="ValidationError"/> naming the overload to use.
/// </para>
/// <para>
/// Content that can be opened again can also be retried.
/// <see cref="FromStream(Stream, long)"/> over a non-seekable stream is read
/// once and gets only that one attempt.
/// </para>
/// </remarks>
public abstract record AssetContent
{
    private protected AssetContent()
    {
    }

    /// <summary>The bytes of a file on disk, opened when the transfer starts.</summary>
    /// <param name="path">The file to upload.</param>
    public static AssetContent FromFile(string path) => new FileContent(path);

    /// <summary>The bytes of a buffer already in memory.</summary>
    /// <param name="bytes">The content to upload.</param>
    public static AssetContent FromBytes(ReadOnlyMemory<byte> bytes) => new BufferContent(bytes);

    /// <summary>
    /// The bytes of a stream the caller owns, read from its current position.
    /// The stream must be seekable, which is what lets it report its length and
    /// be re-read on a retry; the SDK never closes it.
    /// </summary>
    /// <param name="content">The stream to upload from.</param>
    public static AssetContent FromStream(Stream content) => new StreamContent(content, Maybe<long>.None);

    /// <summary>
    /// The bytes of a stream the caller owns, whose length the caller states —
    /// for a stream that cannot report one itself. The SDK never closes it.
    /// </summary>
    /// <param name="content">The stream to upload from.</param>
    /// <param name="length">How many bytes the stream yields.</param>
    public static AssetContent FromStream(Stream content, long length) =>
        new StreamContent(content, Maybe.From(length));

    /// <summary>
    /// A stream opened on demand, once per attempt — the form that keeps a
    /// retry possible for content the SDK cannot reopen itself. The SDK closes
    /// each stream the delegate returns.
    /// </summary>
    /// <param name="open">Opens the content at its start.</param>
    /// <param name="length">How many bytes the stream yields.</param>
    public static AssetContent From(Func<CancellationToken, Task<Stream>> open, long length) =>
        new DelegateContent(open, length);

    /// <summary>
    /// How many bytes this content holds, or why that could not be established.
    /// </summary>
    internal abstract Result<long, OcctooError> ResolveLength();

    /// <summary>
    /// Whether the content can be read again, which is what makes a failed
    /// transfer retryable.
    /// </summary>
    internal abstract bool CanReopen { get; }

    /// <summary>
    /// Opens the content at its start. The caller disposes what comes back;
    /// a stream the consumer owns is wrapped so that disposal leaves it open.
    /// </summary>
    internal abstract Task<Result<Stream, OcctooError>> Open(CancellationToken cancellationToken);

    private sealed record FileContent(string Path) : AssetContent
    {
        internal override bool CanReopen => true;

        internal override Result<long, OcctooError> ResolveLength()
        {
            if (string.IsNullOrWhiteSpace(Path))
                return new ValidationError("A file path is required.");

            try
            {
                var file = new FileInfo(Path);
                return file.Exists
                    ? file.Length
                    : new ValidationError($"The file '{Path}' does not exist.");
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return new ValidationError($"The file '{Path}' could not be read: {exception.Message}");
            }
        }

        internal override Task<Result<Stream, OcctooError>> Open(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return Task.FromResult(Result.Failure<Stream, OcctooError>(
                    new ValidationError("A file path is required.")));
            }

            try
            {
                Stream stream = new FileStream(
                    Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);

                return Task.FromResult(Result.Success<Stream, OcctooError>(stream));
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return Task.FromResult(Result.Failure<Stream, OcctooError>(
                    new ValidationError($"The file '{Path}' could not be opened: {exception.Message}")));
            }
        }
    }

    private sealed record BufferContent(ReadOnlyMemory<byte> Bytes) : AssetContent
    {
        internal override bool CanReopen => true;

        internal override Result<long, OcctooError> ResolveLength() => (long)Bytes.Length;

        internal override Task<Result<Stream, OcctooError>> Open(CancellationToken cancellationToken)
        {
            // The common case — a buffer backed by an array — streams without
            // copying; anything else is copied once.
            Stream stream = MemoryMarshal.TryGetArray(Bytes, out var segment) && segment.Array is not null
                ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
                : new MemoryStream(Bytes.ToArray(), writable: false);

            return Task.FromResult(Result.Success<Stream, OcctooError>(stream));
        }
    }

    private sealed record StreamContent(Stream Content, Maybe<long> StatedLength) : AssetContent
    {
        // Where the stream stood when the caller handed it over: a retry reads
        // from there, not from wherever the failed attempt stopped.
        private readonly long _origin = Content.CanSeek ? Content.Position : 0;

        internal override bool CanReopen => Content.CanSeek;

        internal override Result<long, OcctooError> ResolveLength() => (StatedLength, Content.CanSeek) switch
        {
            ({ HasValue: true } stated, _) => stated.Value,
            (_, true) => Content.Length - _origin,
            _ => new ValidationError(
                "The stream cannot report how many bytes it holds, and Azure Blob Storage rejects an upload of "
                + "unknown length. Pass the length with AssetContent.FromStream(stream, length), or use "
                + "AssetContent.FromFile or AssetContent.FromBytes."),
        };

        internal override Task<Result<Stream, OcctooError>> Open(CancellationToken cancellationToken)
        {
            if (Content.CanSeek)
                Content.Position = _origin;

            // The consumer owns this stream: the transfer disposes what Open
            // returns, so it gets a wrapper that leaves the inner one open.
            return Task.FromResult(Result.Success<Stream, OcctooError>(new LeaveOpenStream(Content)));
        }
    }

    private sealed record DelegateContent(Func<CancellationToken, Task<Stream>> Opener, long Length) : AssetContent
    {
        internal override bool CanReopen => true;

        internal override Result<long, OcctooError> ResolveLength() => Length;

        internal override async Task<Result<Stream, OcctooError>> Open(CancellationToken cancellationToken)
        {
            try
            {
                var stream = await Opener(cancellationToken).ConfigureAwait(false);

                return stream is not null
                    ? stream
                    : new ValidationError("The delegate passed to AssetContent.From returned no stream.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new UnexpectedError($"The content could not be opened: {exception.Message}");
            }
        }
    }
}
