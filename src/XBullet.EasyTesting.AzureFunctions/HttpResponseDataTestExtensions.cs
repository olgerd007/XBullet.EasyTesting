using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Reads in-memory HTTP responses produced by function tests.</summary>
public static class HttpResponseDataTestExtensions
{
    /// <summary>Reads the response body as text without changing its final stream position.</summary>
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
