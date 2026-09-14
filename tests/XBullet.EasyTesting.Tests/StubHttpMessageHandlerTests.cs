using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Http;
using Xunit;

namespace XBullet.EasyTesting.Tests;

public sealed class StubHttpMessageHandlerTests
{
    [Fact]
    public async Task Arranged_response_is_returned_and_request_is_recorded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        handler
            .When(HttpMethod.Post, "/orders?notify=true")
            .RespondJson(new { Accepted = true }, HttpStatusCode.Accepted);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };
        client.DefaultRequestHeaders.Add("X-Tenant", "tenant-42");

        using var response = await client.PostAsJsonAsync(
            "/orders?notify=true",
            new { OrderId = 42 },
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<Response>(cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(body!.Accepted);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/orders?notify=true", request.RequestUri!.PathAndQuery);
        Assert.Equal(["tenant-42"], request.Headers["X-Tenant"]);
        Assert.Contains("\"orderId\":42", request.Body);
    }

    [Fact]
    public async Task Unmatched_request_returns_a_diagnostic_response()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var handler = new StubHttpMessageHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://external.example.test/")
        };

        using var response = await client.GetAsync("/missing", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("No outbound HTTP stub matches GET /missing", body);
        Assert.Equal(1, handler.CallCount);
    }

    private sealed record Response(bool Accepted);
}
