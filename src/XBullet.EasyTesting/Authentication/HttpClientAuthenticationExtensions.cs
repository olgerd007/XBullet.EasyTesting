using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace XBullet.EasyTesting.Authentication;

/// <summary>Methods for selecting the identity used by integration-test requests.</summary>
public static class HttpClientAuthenticationExtensions
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Adds a test identity to every request made by this client.</summary>
    /// <param name="client">
    /// The client whose default request headers are modified in place. The caller retains
    /// ownership and remains responsible for disposing it.
    /// </param>
    /// <param name="user">
    /// The test-user definition serialized immediately into the simulated-authentication header.
    /// The method does not retain, own, or mutate the supplied object. The header is test-only and
    /// is not cryptographically protected.
    /// </param>
    /// <returns>The same <paramref name="client"/> instance for fluent use.</returns>
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
    /// <param name="request">
    /// The request whose headers are modified in place. The caller retains ownership and remains
    /// responsible for disposing it.
    /// </param>
    /// <param name="user">
    /// The test-user definition serialized immediately into the simulated-authentication header.
    /// The method does not retain, own, or mutate the supplied object. The header is test-only and
    /// is not cryptographically protected.
    /// </param>
    /// <returns>The same <paramref name="request"/> instance for fluent use.</returns>
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
    /// <param name="client">
    /// The client whose default simulated-user and Authorization headers are cleared in place.
    /// Request-specific headers, cookies, certificates, and other credentials are unchanged. The
    /// caller retains ownership and remains responsible for disposing the client.
    /// </param>
    /// <returns>The same <paramref name="client"/> instance for fluent use.</returns>
    public static HttpClient UseAnonymousUser(this HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Remove(TestAuthenticationDefaults.UserHeaderName);
        client.DefaultRequestHeaders.Authorization = (AuthenticationHeaderValue?)null;
        return client;
    }
}
