using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XBullet.EasyTesting.Hosting;

/// <summary>Fluently verifies the response produced by a test scenario.</summary>
public sealed class TestHttpResponseAssertions
{
    private static readonly JsonSerializerOptions DefaultJsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly HttpResponseMessage _response;

    internal TestHttpResponseAssertions(HttpResponseMessage response)
    {
        _response = response;
    }

    /// <summary>Requires the response to have the supplied status code.</summary>
    public TestHttpResponseAssertions HaveStatusCode(HttpStatusCode expected)
    {
        if (_response.StatusCode != expected)
        {
            throw Failure(
                $"Expected response status code {Format(expected)}, " +
                $"but it was {Format(_response.StatusCode)}.");
        }

        return this;
    }

    /// <summary>Requires the response to have a successful status code.</summary>
    public TestHttpResponseAssertions BeSuccessful()
    {
        if (!_response.IsSuccessStatusCode)
        {
            throw Failure(
                $"Expected a successful response status code, " +
                $"but it was {Format(_response.StatusCode)}.");
        }

        return this;
    }

    /// <summary>Requires the response or content headers to contain the supplied header.</summary>
    public TestHttpResponseAssertions HaveHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!TryGetHeaderValues(name, out _))
        {
            throw Failure($"Expected response header '{name}', but it was not present.");
        }

        return this;
    }

    /// <summary>Requires the response or content headers to contain the supplied value.</summary>
    public TestHttpResponseAssertions HaveHeader(string name, string expectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(expectedValue);
        if (!TryGetHeaderValues(name, out var values))
        {
            throw Failure($"Expected response header '{name}', but it was not present.");
        }

        if (!values.Contains(expectedValue, StringComparer.Ordinal))
        {
            throw Failure(
                $"Expected response header '{name}' to contain '{expectedValue}', " +
                $"but its value was '{string.Join(", ", values)}'.");
        }

        return this;
    }

    /// <summary>Requires the response body to structurally equal the supplied JSON value.</summary>
    public async Task<TestHttpResponseAssertions> HaveJsonBodyAsync<T>(
        T expected,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var expectedJson = JsonSerializer.SerializeToNode(
            expected,
            options ?? DefaultJsonOptions);
        var actualText = await _response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? actualJson;
        try
        {
            actualJson = JsonNode.Parse(actualText);
        }
        catch (JsonException exception)
        {
            throw Failure(
                $"Expected a JSON response body, but the body was not valid JSON.{Environment.NewLine}" +
                $"Actual body:{Environment.NewLine}{actualText}",
                exception);
        }

        if (!JsonNode.DeepEquals(expectedJson, actualJson))
        {
            throw Failure(
                $"Expected JSON response body:{Environment.NewLine}{Format(expectedJson)}" +
                $"{Environment.NewLine}Actual JSON response body:{Environment.NewLine}{Format(actualJson)}");
        }

        return this;
    }

    private bool TryGetHeaderValues(string name, out IEnumerable<string> values) =>
        _response.Headers.TryGetValues(name, out values!) ||
        _response.Content.Headers.TryGetValues(name, out values!);

    private static string Format(HttpStatusCode statusCode) =>
        $"{(int)statusCode} ({statusCode})";

    private static string Format(JsonNode? value) =>
        value?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";

    private static TestHttpResponseVerificationException Failure(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}

/// <summary>Thrown when a test scenario response does not satisfy an assertion.</summary>
public sealed class TestHttpResponseVerificationException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);
