using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Methods for selecting the identity used by integration-test requests.</summary>
public static class HttpClientAuthenticationExtensions
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Adds a test identity to every request made by this client.</summary>
    public static HttpClient AuthenticateAs(this HttpClient client, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(user);

        var payload = JsonSerializer.SerializeToUtf8Bytes(user, SerializerOptions);
        var headerValue = WebEncoders.Base64UrlEncode(payload);

        client.DefaultRequestHeaders.Remove(TestAuthenticationDefaults.UserHeaderName);
        client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.UserHeaderName, headerValue);
        return client;
    }

    /// <summary>Adds a test identity to one request.</summary>
    public static HttpRequestMessage AuthenticateAs(this HttpRequestMessage request, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        var payload = JsonSerializer.SerializeToUtf8Bytes(user, SerializerOptions);
        var headerValue = WebEncoders.Base64UrlEncode(payload);

        request.Headers.Remove(TestAuthenticationDefaults.UserHeaderName);
        request.Headers.Add(TestAuthenticationDefaults.UserHeaderName, headerValue);
        return request;
    }

    /// <summary>Removes any test identity configured on the client.</summary>
    public static HttpClient UseAnonymousUser(this HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Remove(TestAuthenticationDefaults.UserHeaderName);
        client.DefaultRequestHeaders.Authorization = (AuthenticationHeaderValue?)null;
        return client;
    }
}
