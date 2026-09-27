namespace XBullet.EasyTesting.Hosting;

/// <summary>Owns the client and response produced by an executed test scenario.</summary>
public sealed class TestScenarioResult : IDisposable
{
    private readonly HttpClient _client;

    internal TestScenarioResult(HttpClient client, HttpResponseMessage response)
    {
        _client = client;
        Response = response;
    }

    /// <summary>Gets the HTTP response produced by the scenario.</summary>
    /// <value>
    /// The response owned by this result. Disposing the result also disposes the response; callers
    /// must not use it afterward.
    /// </value>
    public HttpResponseMessage Response { get; }

    /// <summary>Starts a fluent assertion chain over the response.</summary>
    /// <returns>
    /// A new assertion wrapper over <see cref="Response"/>. The wrapper does not own or dispose the
    /// response.
    /// </returns>
    public TestHttpResponseAssertions Should() => new(Response);

    /// <inheritdoc />
    public void Dispose()
    {
        Response.Dispose();
        _client.Dispose();
    }
}
