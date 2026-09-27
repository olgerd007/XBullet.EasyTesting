using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Reads in-memory HTTP responses produced by function tests.</summary>
public static class HttpResponseDataTestExtensions
{
    /// <summary>Reads the response body as text without changing its final stream position.</summary>
    /// <param name="response">The non-null response whose body is read but left open.</param>
    /// <param name="cancellationToken">
    /// A token that cancels reading. The default does not request cancellation. A successful seekable
    /// read restores the original stream position.
    /// </param>
    /// <returns>A task whose result is the complete body decoded as UTF-8 with byte-order-mark detection.</returns>
    public static async Task<string> ReadBodyAsStringAsync(
        this HttpResponseData response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        var originalPosition = response.Body.CanSeek ? response.Body.Position : 0;
        if (response.Body.CanSeek)
        {
            response.Body.Position = 0;
        }

        using var reader = new StreamReader(
            response.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var value = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        if (response.Body.CanSeek)
        {
            response.Body.Position = originalPosition;
        }

        return value;
    }

    /// <summary>Deserializes the response body as JSON.</summary>
    /// <typeparam name="T">The target JSON model type.</typeparam>
    /// <param name="response">The non-null response whose body is read but left open.</param>
    /// <param name="options">JSON options to use, or <see langword="null"/> for new web defaults.</param>
    /// <param name="cancellationToken">
    /// A token that cancels body reading. The default does not request cancellation.
    /// </param>
    /// <returns>
    /// A task whose result is the deserialized value, or <see langword="null"/> when the JSON payload
    /// represents null for a nullable target.
    /// </returns>
    public static async Task<T?> ReadBodyAsJsonAsync<T>(
        this HttpResponseData response,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var json = await response.ReadBodyAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(
            json,
            options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
