using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Provides function instances and fluent trigger data backed by one service provider.</summary>
public sealed class AzureFunctionTestHost : IDisposable, IAsyncDisposable
{
    private readonly ServiceProvider _services;

    internal AzureFunctionTestHost(ServiceProvider services)
    {
        _services = services;
    }

    /// <summary>Starts a function test-host definition.</summary>
    public static AzureFunctionTestHostBuilder CreateBuilder() => new();

    /// <summary>Resolves a registered function or dependency.</summary>
    public T GetRequiredService<T>()
        where T : notnull =>
        _services.GetRequiredService<T>();

    /// <summary>Creates a minimal isolated-worker invocation context.</summary>
    public TestFunctionContext CreateContext(
        string functionName,
        CancellationToken cancellationToken = default) =>
        new(_services, functionName, cancellationToken);

    /// <summary>Starts a fluent HTTP-trigger request for a function.</summary>
    public TestHttpRequestBuilder HttpRequest(
        string functionName,
        CancellationToken cancellationToken = default) =>
        new(CreateContext(functionName, cancellationToken));

    /// <summary>Starts a fluent timer-trigger data definition.</summary>
    public static TestTimerInfoBuilder Timer() => new();

    /// <inheritdoc />
    public void Dispose() => _services.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
