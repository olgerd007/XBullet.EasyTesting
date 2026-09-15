using System.Net;
using System.Text;
using System.Text.Json;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class TestHttpResponseAssertionsTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public TestHttpResponseAssertionsTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Response_assertions_accept_status_and_response_or_content_headers()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        response.Headers.Add("X-Correlation-Id", "test-42");
        using var result = await ExecuteAsync(response);

        var assertions = result.Should();

        Assert.Same(assertions, assertions
            .HaveStatusCode(HttpStatusCode.OK)
            .BeSuccessful()
            .HaveHeader("X-Correlation-Id")
            .HaveHeader("X-Correlation-Id", "test-42")
            .HaveHeader("Content-Type"));
    }

    [Fact]
    public async Task Response_assertions_describe_status_and_header_failures()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        response.Headers.Add("X-Mode", "actual");
        using var result = await ExecuteAsync(response);
        var assertions = result.Should();

        var status = Assert.Throws<TestHttpResponseVerificationException>(
            () => assertions.HaveStatusCode(HttpStatusCode.OK));
        var success = Assert.Throws<TestHttpResponseVerificationException>(
            () => assertions.BeSuccessful());
        var missing = Assert.Throws<TestHttpResponseVerificationException>(
            () => assertions.HaveHeader("X-Missing"));
        var missingWithValue = Assert.Throws<TestHttpResponseVerificationException>(
            () => assertions.HaveHeader("X-Missing", "expected"));
        var wrongValue = Assert.Throws<TestHttpResponseVerificationException>(
            () => assertions.HaveHeader("X-Mode", "expected"));

        Assert.Contains("200 (OK)", status.Message);
        Assert.Contains("400 (BadRequest)", status.Message);
        Assert.Contains("successful", success.Message);
        Assert.Contains("X-Missing", missing.Message);
        Assert.Contains("X-Missing", missingWithValue.Message);
        Assert.Contains("expected", wrongValue.Message);
        Assert.Contains("actual", wrongValue.Message);
        Assert.Throws<ArgumentException>(() => assertions.HaveHeader(" "));
        Assert.Throws<ArgumentNullException>(() => assertions.HaveHeader("X-Mode", null!));
    }

    [Fact]
    public async Task Json_body_assertion_describes_invalid_and_mismatched_json()
    {
        using var invalidResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
        };
        using var invalidResult = await ExecuteAsync(invalidResponse);

        var invalid = await Assert.ThrowsAsync<TestHttpResponseVerificationException>(
            () => invalidResult.Should().HaveJsonBodyAsync(
                new { Id = 42 },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("not valid JSON", invalid.Message);
        Assert.Contains("{not-json", invalid.Message);
        Assert.IsAssignableFrom<JsonException>(invalid.InnerException);

        using var mismatchResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"id\":41}", Encoding.UTF8, "application/json")
        };
        using var mismatchResult = await ExecuteAsync(mismatchResponse);

        var mismatch = await Assert.ThrowsAsync<TestHttpResponseVerificationException>(
            () => mismatchResult.Should().HaveJsonBodyAsync(
                new { Id = 42 },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("\"id\": 42", mismatch.Message);
        Assert.Contains("\"id\": 41", mismatch.Message);
    }

    [Fact]
    public async Task Json_body_assertion_honors_custom_serializer_options()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"order_id\":42}", Encoding.UTF8, "application/json")
        };
        using var result = await ExecuteAsync(response);

        var assertions = result.Should();

        Assert.Same(assertions, await assertions.HaveJsonBodyAsync(
            new ResponseBody(42),
            options,
            TestContext.Current.CancellationToken));
    }

    private Task<TestScenarioResult> ExecuteAsync(HttpResponseMessage response) =>
        _factory.Scenario()
            .Send((_, _) => Task.FromResult(response))
            .ExecuteAsync(TestContext.Current.CancellationToken);

    private sealed record ResponseBody(int OrderId);
}
