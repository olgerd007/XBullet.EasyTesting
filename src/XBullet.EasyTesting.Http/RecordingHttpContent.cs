using System.Net;

namespace XBullet.EasyTesting.Http;

internal sealed class RecordingHttpContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly Action<byte[], Exception?> _completed;

    public RecordingHttpContent(
        HttpContent inner,
        Action<byte[], Exception?> completed)
    {
        _inner = inner;
        _completed = completed;

        foreach (var header in inner.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        CopyAndRecordAsync(stream, context, CancellationToken.None);

    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken) =>
        CopyAndRecordAsync(stream, context, cancellationToken);

    protected override bool TryComputeLength(out long length)
    {
        if (_inner.Headers.ContentLength is long contentLength)
        {
            length = contentLength;
            return true;
        }

        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task CopyAndRecordAsync(
        Stream destination,
        TransportContext? context,
        CancellationToken cancellationToken)
    {
        using var capture = new MemoryStream();
        await using var recordingStream = new RecordingWriteStream(destination, capture);
        try
        {
            await _inner.CopyToAsync(recordingStream, context, cancellationToken);
            _completed(capture.ToArray(), null);
        }
        catch (Exception exception)
        {
            _completed(capture.ToArray(), exception);
            throw;
        }
    }

    private sealed class RecordingWriteStream(Stream destination, Stream capture) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => destination.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            destination.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            destination.Write(buffer, offset, count);
            capture.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            destination.Write(buffer);
            capture.Write(buffer);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await destination.WriteAsync(buffer, cancellationToken);
            await capture.WriteAsync(buffer, cancellationToken);
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            await capture.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // The destination and capture streams are owned by the caller.
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
