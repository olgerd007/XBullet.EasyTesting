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
    /// <value>The Azure pipeline request method captured before the response was created.</value>
    public RequestMethod Method { get; }

    /// <summary>Gets the complete request URI.</summary>
    /// <value>
    /// The absolute request URI, including its query string. Query values can contain credentials or
    /// other sensitive data; unlike scenario diagnostics, this raw record is not redacted.
    /// </value>
    public Uri Uri { get; }

    /// <summary>Gets captured request headers using case-insensitive keys.</summary>
    /// <value>
    /// A stable, read-only snapshot keyed case-insensitively. Header values are not redacted and can
    /// contain sensitive data.
    /// </value>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Gets the buffered request body, when present.</summary>
    /// <value>
    /// An owned buffer containing the request body, or <see langword="null"/> when the request had no
    /// content. The content is not redacted and can contain sensitive data.
    /// </value>
    public BinaryData? Content { get; }
}
