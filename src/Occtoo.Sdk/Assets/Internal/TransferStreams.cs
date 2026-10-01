namespace Occtoo.Assets.Internal;

/// <summary>
/// Hands a stream to the transfer without handing over its lifetime: the
/// request disposes the content it was given, and a stream the consumer owns
/// must survive that.
/// </summary>
internal sealed class LeaveOpenStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// Counts the bytes the transfer reads and reports them as they go, at most
/// once every <see cref="ThrottleMilliseconds"/> — a large file would otherwise
/// drown the consumer in reports. The count that completes the content is
/// always reported.
/// </summary>
internal sealed class ProgressStream(Stream inner, long totalBytes, Action<long> report) : Stream
{
    private const int ThrottleMilliseconds = 100;

    private long _transferred;
    private long _lastReportedAt = Environment.TickCount64;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => totalBytes;

    public override long Position
    {
        get => _transferred;
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => Advance(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Advance(inner.Read(buffer));

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Advance(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Advance(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Advance(int read)
    {
        if (read <= 0)
            return read;

        _transferred += read;

        var now = Environment.TickCount64;
        if (_transferred < totalBytes && now - _lastReportedAt < ThrottleMilliseconds)
            return read;

        _lastReportedAt = now;
        report(_transferred);
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
