namespace XBullet.EasyTesting.Http;

/// <summary>A captured outbound HTTP request received by <see cref="StubHttpMessageHandler"/>.</summary>
public sealed record StubHttpRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyDictionary<string, string[]> Headers,
    string? Body);
