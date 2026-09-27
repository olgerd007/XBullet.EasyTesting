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
    /// <param name="expected">The exact HTTP status code required.</param>
    /// <returns>This assertion object so additional checks can be chained.</returns>
    /// <exception cref="TestHttpResponseVerificationException">
    /// The response has a different status code.
    /// </exception>
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
    /// <returns>This assertion object so additional checks can be chained.</returns>
    /// <exception cref="TestHttpResponseVerificationException">
    /// The response status code is outside the inclusive 200 through 299 range.
    /// </exception>
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
    /// <param name="name">
    /// The non-empty header name to find. Header-name matching follows the case-insensitive HTTP
    /// header rules implemented by <see cref="System.Net.Http.Headers.HttpHeaders"/>.
    /// </param>
    /// <returns>This assertion object so additional checks can be chained.</returns>
    /// <exception cref="TestHttpResponseVerificationException">
    /// Neither the response headers nor content headers contain <paramref name="name"/>.
    /// </exception>
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
    /// <param name="name">
    /// The non-empty header name to find. Header-name matching follows the case-insensitive HTTP
    /// header rules implemented by <see cref="System.Net.Http.Headers.HttpHeaders"/>.
    /// </param>
    /// <param name="expectedValue">
    /// The exact non-null value required among the header's parsed values. Value comparison is
    /// ordinal and case-sensitive; an empty value is accepted.
    /// </param>
    /// <returns>This assertion object so additional checks can be chained.</returns>
    /// <exception cref="TestHttpResponseVerificationException">
    /// The header is absent or none of its values equals <paramref name="expectedValue"/>.
    /// </exception>
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
    /// <typeparam name="T">The type of value serialized to produce the expected JSON document.</typeparam>
    /// <param name="expected">
    /// The value to serialize and compare structurally with the response body. JSON object-property
    /// order and insignificant whitespace do not affect the comparison.
    /// </param>
    /// <param name="options">
    /// Serialization options for <paramref name="expected"/>. When <see langword="null"/>, web
    /// defaults are used. The options object is read but not owned or mutated.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels reading the response content. Cancellation does not dispose or otherwise mutate the
    /// response. The default token does not request cancellation.
    /// </param>
    /// <returns>A task whose result is this assertion object for further chaining.</returns>
    /// <exception cref="TestHttpResponseVerificationException">
    /// The response body is invalid JSON or is structurally different from the expected JSON.
    /// </exception>
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
/// <param name="message">The assertion-failure message presented to the test runner.</param>
/// <param name="innerException">
/// The underlying parsing or processing failure, or <see langword="null"/> when the assertion did
/// not fail because of another exception. The exception retains but does not dispose this value.
/// </param>
public sealed class TestHttpResponseVerificationException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);
