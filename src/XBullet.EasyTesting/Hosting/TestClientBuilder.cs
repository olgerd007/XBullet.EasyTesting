using XBullet.EasyTesting.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;

namespace XBullet.EasyTesting.Hosting;

/// <summary>Fluently configures a client created by an authenticated application factory.</summary>
public sealed class TestClientBuilder<TEntryPoint>
    where TEntryPoint : class
{
    private readonly AuthenticatedWebApplicationFactory<TEntryPoint> _factory;
    private readonly WebApplicationFactoryClientOptions _options = new();
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    private TestUser? _user;

    internal TestClientBuilder(AuthenticatedWebApplicationFactory<TEntryPoint> factory)
    {
        _factory = factory;
    }

    /// <summary>Configures the client to send the supplied test identity.</summary>
    public TestClientBuilder<TEntryPoint> AsUser(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        _user = user;
        return this;
    }

    /// <summary>Builds and configures the test identity used by this client.</summary>
    public TestClientBuilder<TEntryPoint> AsUser(Action<TestUserBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = TestUser.CreateBuilder();
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Builds an Azure AD-shaped identity for this client.</summary>
    public TestClientBuilder<TEntryPoint> AsAzureAdUser(Action<TestAzureAdUserBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TestAzureAdUserBuilder(_factory.GetAzureAdAuthenticationScheme());
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Builds an API-key-shaped identity for this client.</summary>
    public TestClientBuilder<TEntryPoint> AsApiKey(Action<TestApiKeyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TestApiKeyBuilder(_factory.GetApiKeyAuthenticationScheme());
        configure(builder);
        return AsUser(builder.Build());
    }

    /// <summary>Configures the client to send no test identity.</summary>
    public TestClientBuilder<TEntryPoint> AsAnonymous()
    {
        _user = null;
        return this;
    }

    /// <summary>Sets the base address used by relative requests.</summary>
    public TestClientBuilder<TEntryPoint> WithBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _options.BaseAddress = baseAddress;
        return this;
    }

    /// <summary>Prevents automatic HTTP redirect handling.</summary>
    public TestClientBuilder<TEntryPoint> WithoutRedirects()
    {
        _options.AllowAutoRedirect = false;
        return this;
    }

    /// <summary>Prevents automatic cookie persistence.</summary>
    public TestClientBuilder<TEntryPoint> WithoutCookies()
    {
        _options.HandleCookies = false;
        return this;
    }

    /// <summary>Adds or replaces a default request header.</summary>
    public TestClientBuilder<TEntryPoint> WithHeader(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _headers[name] = value;
        return this;
    }

    /// <summary>Creates the configured client.</summary>
    public HttpClient Build()
    {
        var client = _factory.CreateAnonymousClient(_options);
        if (_user is not null)
        {
            client.AuthenticateAs(_user);
        }

        foreach (var header in _headers)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }
}
