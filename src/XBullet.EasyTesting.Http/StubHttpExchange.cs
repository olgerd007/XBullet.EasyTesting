namespace XBullet.EasyTesting.Http;

/// <summary>A captured outbound HTTP request and its response or failure.</summary>
public sealed class StubHttpExchange
{
    private readonly object _gate = new();
    private StubHttpResponse? _response;
    private StubHttpFailure? _failure;

    internal StubHttpExchange(StubHttpRequest request) => Request = request;

    /// <summary>Gets the captured request.</summary>
    public StubHttpRequest Request { get; }

    /// <summary>Gets the response returned by the stub, when one was produced.</summary>
    public StubHttpResponse? Response
    {
        get
        {
            lock (_gate)
            {
                return _response;
            }
        }
    }

    /// <summary>Gets the failure raised before a response was produced.</summary>
    public StubHttpFailure? Failure
    {
        get
        {
            lock (_gate)
            {
                return _failure;
            }
        }
    }

    internal void SetResponse(StubHttpResponse response)
    {
        lock (_gate)
        {
            _response = response;
        }
    }

    internal void SetFailure(Exception exception)
    {
        lock (_gate)
        {
            _failure = StubHttpFailure.FromException(exception);
        }
    }

    internal void SetResponseBody(byte[] body, Exception? exception)
    {
        lock (_gate)
        {
            if (_response is null)
            {
                return;
            }

            _response = _response with
            {
                BodyCaptured = true,
                Body = body,
                BodyFailure = exception is null
                    ? null
                    : StubHttpFailure.FromException(exception.GetBaseException())
            };
        }
    }
}

/// <summary>A captured outbound HTTP response.</summary>
public sealed record StubHttpResponse(
    int StatusCode,
    string? ReasonPhrase,
    IReadOnlyDictionary<string, string[]> Headers,
    bool BodyCaptured,
    ReadOnlyMemory<byte> Body,
    StubHttpFailure? BodyFailure);

/// <summary>A stable description of an exception observed during an HTTP exchange.</summary>
public sealed record StubHttpFailure(string Type, string Message)
{
    internal static StubHttpFailure FromException(Exception exception) =>
        new(exception.GetType().FullName ?? exception.GetType().Name, exception.Message);
}
