using System.Net;

namespace XBullet.EasyTesting.Snapshots;

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

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
        new RecordingReadStream(
            _inner.ReadAsStream(cancellationToken),
            _inner.Headers.ContentLength,
            _completed);

    protected override async Task<Stream> CreateContentReadStreamAsync() =>
        new RecordingReadStream(
            await _inner.ReadAsStreamAsync(),
            _inner.Headers.ContentLength,
            _completed);

    protected override async Task<Stream> CreateContentReadStreamAsync(
        CancellationToken cancellationToken) =>
        new RecordingReadStream(
            await _inner.ReadAsStreamAsync(cancellationToken),
            _inner.Headers.ContentLength,
            _completed);

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

    private sealed class RecordingReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long? _expectedLength;
        private readonly MemoryStream _capture = new();
        private Action<byte[], Exception?>? _completed;
        private int _disposed;

        public RecordingReadStream(
            Stream inner,
            long? expectedLength,
            Action<byte[], Exception?> completed)
        {
            _inner = inner;
            _expectedLength = expectedLength;
            _completed = completed;
            if (expectedLength == 0)
            {
                Complete(null);
            }
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override bool CanTimeout => _inner.CanTimeout;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int ReadTimeout
        {
            get => _inner.ReadTimeout;
            set => _inner.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            get => _inner.WriteTimeout;
            set => _inner.WriteTimeout = value;
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                var bytesRead = _inner.Read(buffer, offset, count);
                Record(buffer.AsSpan(offset, bytesRead));
                return bytesRead;
            }
            catch (Exception exception)
            {
                Complete(exception);
                throw;
            }
        }

        public override int Read(Span<byte> buffer)
        {
            try
            {
                var bytesRead = _inner.Read(buffer);
                Record(buffer[..bytesRead]);
                return bytesRead;
            }
            catch (Exception exception)
            {
                Complete(exception);
                throw;
            }
        }

        public override int ReadByte()
        {
            try
            {
                var value = _inner.ReadByte();
                if (value < 0)
                {
                    Complete(null);
                }
                else
                {
                    _capture.WriteByte((byte)value);
                    CompleteWhenExpectedLengthIsReached();
                }

                return value;
            }
            catch (Exception exception)
            {
                Complete(exception);
                throw;
            }
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            try
            {
                var bytesRead = await _inner.ReadAsync(
                    buffer.AsMemory(offset, count),
                    cancellationToken);
                Record(buffer.AsSpan(offset, bytesRead));
                return bytesRead;
            }
            catch (Exception exception)
            {
                Complete(exception);
                throw;
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var bytesRead = await _inner.ReadAsync(buffer, cancellationToken);
                Record(buffer.Span[..bytesRead]);
                return bytesRead;
            }
            catch (Exception exception)
            {
                Complete(exception);
                throw;
            }
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            _inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try
                {
                    _inner.Dispose();
                    CompleteOnDispose();
                }
                catch (Exception exception)
                {
                    Complete(exception);
                    throw;
                }
                finally
                {
                    _capture.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await _inner.DisposeAsync();
                CompleteOnDispose();
            }
            catch (Exception exception)
            {
                Complete(exception);
                throw;
            }
            finally
            {
                _capture.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        private void Record(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                Complete(null);
                return;
            }

            _capture.Write(bytes);
            CompleteWhenExpectedLengthIsReached();
        }

        private void CompleteWhenExpectedLengthIsReached()
        {
            if (_expectedLength is long expectedLength && _capture.Length >= expectedLength)
            {
                Complete(null);
            }
        }

        private void CompleteOnDispose()
        {
            if (_capture.Length > 0 || _expectedLength == 0)
            {
                Complete(null);
                return;
            }

            Interlocked.Exchange(ref _completed, null);
        }

        private void Complete(Exception? exception)
        {
            var completed = Interlocked.Exchange(ref _completed, null);
            completed?.Invoke(_capture.ToArray(), exception);
        }
    }
}
