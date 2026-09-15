using Azure.Core;

namespace XBullet.EasyTesting.Azure;

/// <summary>An immutable Azure SDK pipeline request captured by a stub transport.</summary>
public sealed class RecordedAzureRequest
{
    internal RecordedAzureRequest(
        RequestMethod method,
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        BinaryData? content)
    {
        Method = method;
        Uri = uri;
        Headers = headers;
        Content = content;
    }

    /// <summary>Gets the request method.</summary>
    public RequestMethod Method { get; }

    /// <summary>Gets the complete request URI.</summary>
    public Uri Uri { get; }

    /// <summary>Gets captured request headers using case-insensitive keys.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Gets the buffered request body, when present.</summary>
    public BinaryData? Content { get; }
}
