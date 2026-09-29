using System.Net;

namespace XBullet.EasyTesting.Http;

internal sealed class RecordingHttpContent : HttpContent
{
    private const int MaximumInitialCapacity = 1_048_576;
    private readonly HttpContent _inner;
    private readonly int? _maximumCaptureBytes;
    private readonly Action<byte[], bool, Exception?> _completed;

    public RecordingHttpContent(
        HttpContent inner,
        int? maximumCaptureBytes,
        Action<byte[], bool, Exception?> completed)
    {
        _inner = inner;
        _maximumCaptureBytes = maximumCaptureBytes;
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
        var capacity = _inner.Headers.ContentLength is long contentLength
            ? (int)Math.Min(
                Math.Min(contentLength, MaximumInitialCapacity),
                _maximumCaptureBytes is int maximum ? maximum : MaximumInitialCapacity)
            : 0;
        using var capture = new MemoryStream(capacity);
        await using var recordingStream = new RecordingWriteStream(
            destination,
            capture,
            _maximumCaptureBytes);
        try
        {
            await _inner.CopyToAsync(recordingStream, context, cancellationToken);
            _completed(capture.ToArray(), recordingStream.Truncated, null);
        }
        catch (Exception exception)
        {
            _completed(capture.ToArray(), recordingStream.Truncated, exception);
            throw;
        }
    }

    private sealed class RecordingWriteStream(
        Stream destination,
        Stream capture,
        int? maximumCaptureBytes) : Stream
    {
        public bool Truncated { get; private set; }

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
            Capture(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            destination.Write(buffer);
            Capture(buffer);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await destination.WriteAsync(buffer, cancellationToken);
            Capture(buffer.Span);
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            Capture(buffer.AsSpan(offset, count));
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

        private void Capture(ReadOnlySpan<byte> buffer)
        {
            if (maximumCaptureBytes is null)
            {
                capture.Write(buffer);
                return;
            }

            var remaining = maximumCaptureBytes.Value - (int)capture.Length;
            if (remaining > 0)
            {
                capture.Write(buffer[..Math.Min(buffer.Length, remaining)]);
            }

            if (buffer.Length > remaining)
            {
                Truncated = true;
            }
        }
    }
}
