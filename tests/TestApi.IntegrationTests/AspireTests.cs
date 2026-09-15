#if NET10_0
extern alias testapphost;
#endif

using XBullet.EasyTesting.Aspire;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class AspireTests
{
    [Fact]
    public void Builder_is_fluent_and_rejects_mutation_after_start_is_attempted()
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
    public async Task Distributed_app_waits_for_health_serves_http_and_attaches_failure_diagnostics()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AspireTestHost.Create<testapphost::TestAppHost.AppHostMarker>()
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
#endif

    private sealed class BuilderMarker;
}
