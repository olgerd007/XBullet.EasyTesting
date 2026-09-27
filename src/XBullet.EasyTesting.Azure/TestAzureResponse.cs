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
    /// <param name="status">The HTTP status code from 100 through 599. The default is 200.</param>
    /// <param name="reasonPhrase">
    /// The reason phrase, or <see langword="null"/> to derive the standard phrase for known status
    /// codes. Unknown codes receive an empty phrase. An explicitly empty phrase is preserved.
    /// </param>
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
    /// <param name="name">The non-empty, non-whitespace header name. Names are matched case-insensitively.</param>
    /// <param name="value">The non-null header value. An empty value is accepted.</param>
    /// <returns>This response, for chaining. Any existing values for the header are replaced.</returns>
    public TestAzureResponse WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _headers[name] = [value];
        return this;
    }

    /// <summary>Adds or replaces a multi-value response header.</summary>
    /// <param name="name">The non-empty, non-whitespace header name. Names are matched case-insensitively.</param>
    /// <param name="values">
    /// The non-null array of non-null header values. Empty strings and an empty array are accepted;
    /// the array is copied before this method returns.
    /// </param>
    /// <returns>This response, for chaining. Any existing values for the header are replaced.</returns>
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
    /// <param name="content">
    /// The non-null binary content to copy into a new read-only stream. The caller retains ownership
    /// of the supplied value.
    /// </param>
    /// <param name="contentType">
    /// The content-type header to add or replace, or <see langword="null"/>, empty, or whitespace to
    /// leave that header unchanged. The content-length header is always replaced with the byte count.
    /// </param>
    /// <returns>
    /// This response, for chaining. Any previous content stream is disposed and replaced immediately.
    /// </returns>
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
    /// <typeparam name="T">The type of value to serialize.</typeparam>
    /// <param name="value">The value to serialize; nullable values are accepted when <typeparamref name="T"/> permits them.</param>
    /// <param name="serializerOptions">
    /// The JSON options to use, or <see langword="null"/> to use Azure <see cref="BinaryData"/> defaults.
    /// </param>
    /// <returns>
    /// This response, for chaining, with a copied JSON body, an <c>application/json</c> content type,
    /// and a matching content length. Any previous content stream is disposed.
    /// </returns>
    public TestAzureResponse WithJsonContent<T>(
        T value,
        JsonSerializerOptions? serializerOptions = null) =>
        WithContent(
            BinaryData.FromObjectAsJson(value, serializerOptions),
            "application/json");

    /// <summary>Wraps a model value and this raw response in an Azure <see cref="Response{T}"/>.</summary>
    /// <typeparam name="T">The type of model value carried by the response.</typeparam>
    /// <param name="value">The model value to retain; nullable values are accepted when <typeparamref name="T"/> permits them.</param>
    /// <returns>
    /// A model response containing <paramref name="value"/> and this exact raw response. This response
    /// remains caller-owned and must not be disposed while the wrapper is in use.
    /// </returns>
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
