namespace XBullet.EasyTesting.Http;

/// <summary>A captured outbound HTTP request received by <see cref="StubHttpMessageHandler"/>.</summary>
/// <param name="Method">
/// The non-null HTTP method from the outbound request. The snapshot does not own or mutate it.
/// </param>
/// <param name="RequestUri">
/// The request URI as observed by the handler, or <see langword="null"/> when the outbound request
/// had no URI. Query values are stored without redaction.
/// </param>
/// <param name="Headers">
/// A case-insensitive snapshot containing request and content headers. The handler owns the
/// dictionary and nested arrays; callers must not mutate them. Values are not redacted.
/// </param>
/// <param name="Body">
/// The request content captured as text, or <see langword="null"/> when the request had no content.
/// An empty content body is represented by an empty string. Values are not redacted.
/// </param>
public sealed record StubHttpRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyDictionary<string, string[]> Headers,
    string? Body);
