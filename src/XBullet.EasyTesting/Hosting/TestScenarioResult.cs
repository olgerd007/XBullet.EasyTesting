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
    public HttpResponseMessage Response { get; }

    /// <summary>Starts a fluent assertion chain over the response.</summary>
    public TestHttpResponseAssertions Should() => new(Response);

    /// <inheritdoc />
    public void Dispose()
    {
        Response.Dispose();
        _client.Dispose();
    }
}
