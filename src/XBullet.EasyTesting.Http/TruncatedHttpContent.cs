using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace XBullet.EasyTesting.Http;

internal sealed class TruncatedHttpContent : HttpContent
{
    private readonly byte[] _partialContent;

    public TruncatedHttpContent(string partialContent, string mediaType)
    {
        _partialContent = Encoding.UTF8.GetBytes(partialContent);
        Headers.ContentType = new MediaTypeHeaderValue(mediaType)
        {
            CharSet = Encoding.UTF8.WebName
        };
    }

    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        await stream.WriteAsync(_partialContent);
        throw new IOException("The arranged outbound HTTP response ended before its content was complete.");
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _partialContent.Length + 1;
        return true;
    }
}
