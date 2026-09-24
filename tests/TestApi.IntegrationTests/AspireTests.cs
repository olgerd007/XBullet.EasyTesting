#if NET10_0
extern alias testapphost;
#endif

using XBullet.EasyTesting.Aspire;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class AspireTests
{
    [Fact]
    public void Builder_is_fluent_and_validates_configuration()
    {
        var builder = AspireTestHost.Create<BuilderMarker>();

        Assert.Same(builder, builder.WithArguments("--environment=Testing"));
        Assert.Same(builder, builder.ConfigureAppHost(_ => { }));
        Assert.Same(builder, builder.WaitForResource("api"));
        Assert.Same(builder, builder.WaitForResource("API"));
        Assert.Same(builder, builder.WithStartupTimeout(TimeSpan.FromSeconds(30)));
        Assert.Same(builder, builder.WithMaximumDiagnosticLinesPerResource(25));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.WithStartupTimeout(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.WithMaximumDiagnosticLinesPerResource(-1));
        Assert.Throws<ArgumentNullException>(() => builder.WithArguments(null!));
        Assert.Throws<ArgumentNullException>(() => builder.WithArguments([null!]));
        Assert.Throws<ArgumentNullException>(() => builder.ConfigureAppHost(null!));
        Assert.Throws<ArgumentException>(() => builder.WaitForResource(" "));

        var firstFailure = new InvalidOperationException("first");
        var cleanupFailures = AspireTestApplication<BuilderMarker>.AddCleanupFailure(
            failures: null,
            firstFailure);
        Assert.Same(
            cleanupFailures,
            AspireTestApplication<BuilderMarker>.AddCleanupFailure(
                cleanupFailures,
                new InvalidOperationException("second")));
        Assert.Equal(2, cleanupFailures.Count);
    }

    [Fact]
    public void Diagnostics_preserve_resource_state_and_stream_identity()
    {
        var log = new AspireResourceLogEntry(42, "service ready", isError: false);
        var resource = new AspireResourceDiagnostics(
            "api",
            "api-1234",
            "project.v0",
            "Running",
            "Healthy",
            exitCode: null,
            [log]);
        var diagnostics = new AspireApplicationDiagnostics(
            DateTimeOffset.UtcNow,
            [resource]);

        Assert.Same(resource, Assert.Single(diagnostics.Resources));
        Assert.Same(log, Assert.Single(resource.Logs));
        Assert.Equal(42, log.LineNumber);
        Assert.False(log.IsError);
    }

#if NET10_0
    [Fact(Explicit = true)]
    public async Task Startup_timeout_is_reported_as_timeout_and_cleans_partial_state()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            AspireTestHost.Create<testapphost::TestAppHost.AppHostMarker>()
                .WithStartupTimeout(TimeSpan.FromMilliseconds(1))
                .StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Explicit = true)]
    public async Task Caller_cancellation_is_preserved_instead_of_reported_as_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AspireTestHost.Create<testapphost::TestAppHost.AppHostMarker>()
                .StartAsync(cancellation.Token));

        Assert.IsNotType<TimeoutException>(exception);
    }

    [Fact(Explicit = true)]
    public async Task Distributed_app_waits_for_health_serves_http_and_attaches_failure_diagnostics()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AspireTestHost.Create<testapphost::TestAppHost.AppHostMarker>()
                .WithArguments("--environment=Testing")
                .ConfigureAppHost(_ => { })
                .WaitForResource("api")
                .WithStartupTimeout(TimeSpan.FromMinutes(2))
                .RunAsync(
                    async (application, cancellationToken) =>
                    {
                        Assert.Contains("api", application.ResourceNames);
                        Assert.NotNull(application.GetEndpoint("api"));
                        using var client = application.CreateHttpClient("api");
                        using var response = await client.GetAsync("/health", cancellationToken);
                        response.EnsureSuccessStatusCode();
                        Assert.Contains(
                            "Healthy",
                            await response.Content.ReadAsStringAsync(cancellationToken));
                        throw new InvalidOperationException("Expected distributed test failure.");
                    },
                    TestContext.Current.CancellationToken));

        var diagnostics = Assert.IsType<AspireApplicationDiagnostics>(
            exception.Data[AspireApplicationDiagnostics.ExceptionDataKey]);
        var api = Assert.Single(diagnostics.Resources, resource => resource.Name == "api");
        Assert.Equal("Healthy", api.HealthStatus);
        Assert.NotEmpty(api.Logs);
    }

    [Fact(Explicit = true)]
    public async Task Distributed_app_exposes_all_resource_operations_and_disposes_idempotently()
    {
        var builder = AspireTestHost.Create<testapphost::TestAppHost.AppHostMarker>()
            .ConfigureAppHost(_ => { })
            .WaitForResource("api")
            .WithMaximumDiagnosticLinesPerResource(1);
        var application = await builder.StartAsync(TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => builder.WithArguments("--late"));
        Assert.Throws<InvalidOperationException>(() => builder.ConfigureAppHost(_ => { }));
        Assert.Throws<InvalidOperationException>(() => builder.WaitForResource("api"));
        Assert.Throws<InvalidOperationException>(() =>
            builder.WithStartupTimeout(TimeSpan.FromSeconds(1)));
        Assert.Throws<InvalidOperationException>(() =>
            builder.WithMaximumDiagnosticLinesPerResource(1));

        Assert.Contains("api", application.ResourceNames);
        Assert.NotNull(application.GetEndpoint("api"));
        Assert.NotNull(application.GetEndpoint("api", "http"));
        Assert.Throws<ArgumentException>(() => application.GetEndpoint("api", " "));
        await application.WaitForResourceAsync("api", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await application.GetConnectionStringAsync(
                "api",
                TestContext.Current.CancellationToken));
        using (var client = application.CreateHttpClient("api"))
        using (var response = await client.GetAsync("/health", TestContext.Current.CancellationToken))
        {
            response.EnsureSuccessStatusCode();
        }

        using (var client = application.CreateHttpClient("api", "http"))
        using (var response = await client.GetAsync("/health", TestContext.Current.CancellationToken))
        {
            response.EnsureSuccessStatusCode();
        }

        var diagnostics = await application.CaptureDiagnosticsAsync(
            TestContext.Current.CancellationToken);
        Assert.All(diagnostics.Resources, resource => Assert.True(resource.Logs.Count <= 1));

        await application.DisposeAsync();
        await application.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => application.GetEndpoint("api"));
    }
#endif

    private sealed class BuilderMarker;
}
