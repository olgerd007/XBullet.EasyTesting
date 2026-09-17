using System.Net;
using System.Text.Json;
using Azure;
using Azure.Core;

namespace XBullet.EasyTesting.Azure;

/// <summary>A concrete, configurable Azure SDK response for tests.</summary>
public sealed class TestAzureResponse : Response
{
    private readonly Dictionary<string, List<string>> _headers =
        new(StringComparer.OrdinalIgnoreCase);
    private Stream? _contentStream;

    /// <summary>Creates a response with the supplied HTTP status.</summary>
    public TestAzureResponse(int status = 200, string? reasonPhrase = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(status, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599);
        Status = status;
        ReasonPhrase = reasonPhrase ?? GetReasonPhrase(status);
    }

    /// <inheritdoc />
    public override int Status { get; }

    /// <inheritdoc />
    public override string ReasonPhrase { get; }

    /// <inheritdoc />
    public override Stream? ContentStream
    {
        get => _contentStream;
        set => _contentStream = value;
    }

    /// <inheritdoc />
    public override string ClientRequestId { get; set; } = string.Empty;

    /// <summary>Adds or replaces a response header.</summary>
    public TestAzureResponse WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _headers[name] = [value];
        return this;
    }

    /// <summary>Adds or replaces a multi-value response header.</summary>
    public TestAzureResponse WithHeader(string name, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Any(value => value is null))
        {
            throw new ArgumentException("Header values cannot contain null.", nameof(values));
        }

        _headers[name] = [.. values];
        return this;
    }

    /// <summary>Sets the binary response body and optional content type.</summary>
    public TestAzureResponse WithContent(BinaryData content, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _contentStream?.Dispose();
        var bytes = content.ToArray();
        _contentStream = new MemoryStream(bytes, writable: false);
        WithHeader("Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            WithHeader("Content-Type", contentType);
        }

        return this;
    }

    /// <summary>Serializes a JSON response body.</summary>
    public TestAzureResponse WithJsonContent<T>(
        T value,
        JsonSerializerOptions? serializerOptions = null) =>
        WithContent(
            BinaryData.FromObjectAsJson(value, serializerOptions),
            "application/json");

    /// <summary>Wraps a model value and this raw response in an Azure <see cref="Response{T}"/>.</summary>
    public Response<T> FromValue<T>(T value) => Response.FromValue(value, this);

    /// <inheritdoc />
    public override void Dispose()
    {
        _contentStream?.Dispose();
        _contentStream = null;
    }

    /// <inheritdoc />
    protected override bool ContainsHeader(string name) => _headers.ContainsKey(name);

    /// <inheritdoc />
    protected override IEnumerable<HttpHeader> EnumerateHeaders() =>
        _headers.Select(header => new HttpHeader(header.Key, string.Join(',', header.Value)));

    /// <inheritdoc />
    protected override bool TryGetHeader(string name, out string value)
    {
        if (_headers.TryGetValue(name, out var values))
        {
            value = string.Join(',', values);
            return true;
        }

        value = null!;
        return false;
    }

    /// <inheritdoc />
    protected override bool TryGetHeaderValues(
        string name,
        out IEnumerable<string> values)
    {
        if (_headers.TryGetValue(name, out var storedValues))
        {
            values = storedValues.ToArray();
            return true;
        }

        values = null!;
        return false;
    }

    private static string GetReasonPhrase(int status) =>
        Enum.IsDefined(typeof(HttpStatusCode), status)
            ? ((HttpStatusCode)status).ToString()
            : string.Empty;
}
