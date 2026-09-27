namespace XBullet.EasyTesting.Http;

/// <summary>A captured outbound HTTP request and its response or failure.</summary>
public sealed class StubHttpExchange
{
    private readonly object _gate = new();
    private StubHttpResponse? _response;
    private StubHttpFailure? _failure;

    internal StubHttpExchange(StubHttpRequest request) => Request = request;

    /// <summary>Gets the captured request.</summary>
    /// <value>
    /// The handler-owned request snapshot for this exchange. Callers must not mutate its nested
    /// header values. Raw values are not redacted.
    /// </value>
    public StubHttpRequest Request { get; }

    /// <summary>Gets the response returned by the stub, when one was produced.</summary>
    /// <value>
    /// The latest immutable response snapshot, or <see langword="null"/> before a response is
    /// produced or when request processing fails. Query this property again after consuming content
    /// to observe captured body bytes or a body-read failure.
    /// </value>
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
    /// <value>
    /// A stable failure description when matching, delay, or response creation failed; otherwise,
    /// <see langword="null"/>. Failures raised while consuming response content appear on
    /// <see cref="StubHttpResponse.BodyFailure"/> instead.
    /// </value>
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
/// <param name="StatusCode">The numeric HTTP status code returned by the stub.</param>
/// <param name="ReasonPhrase">
/// The optional HTTP reason phrase, or <see langword="null"/> when none was supplied.
/// </param>
/// <param name="Headers">
/// A case-insensitive snapshot of response and content headers. The exchange owns the dictionary
/// and nested arrays; callers must not mutate them.
/// </param>
/// <param name="BodyCaptured">
/// <see langword="true"/> when the response has no content or content consumption has completed or
/// failed; otherwise, <see langword="false"/>.
/// </param>
/// <param name="Body">
/// The response bytes captured during content consumption. Before consumption this is empty; after
/// a failed read it contains the bytes written before failure. The exchange owns the memory.
/// </param>
/// <param name="BodyFailure">
/// A stable description of the content-consumption failure, or <see langword="null"/> when content
/// has not been consumed or was consumed successfully.
/// </param>
public sealed record StubHttpResponse(
    int StatusCode,
    string? ReasonPhrase,
    IReadOnlyDictionary<string, string[]> Headers,
    bool BodyCaptured,
    ReadOnlyMemory<byte> Body,
    StubHttpFailure? BodyFailure);

/// <summary>A stable description of an exception observed during an HTTP exchange.</summary>
/// <param name="Type">The non-empty fully qualified exception type name when available.</param>
/// <param name="Message">The exception message captured at the time of failure.</param>
public sealed record StubHttpFailure(string Type, string Message)
{
    internal static StubHttpFailure FromException(Exception exception) =>
        new(exception.GetType().FullName ?? exception.GetType().Name, exception.Message);
}
