using System.Text.Json;
using XBullet.EasyTesting.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class ScenarioEnvironmentResourceTests
{
    [Fact]
    public async Task Environment_resource_starts_before_host_configuration_and_is_disposed_after_host()
    {
        var events = new List<string>();
        RecordingEnvironmentResource? resource = null;
        using var factory = EasyTestHost.Create<Program>()
            .ConfigureEnvironment(environment => environment.AddResource(
                "dependency",
                context => resource = new RecordingEnvironmentResource(
                    context.ScenarioId,
                    events)))
            .Build();

        await using (var scope = await factory.CreateTestScenarioScopeAsync(
            cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.NotNull(resource);
            Assert.Same(resource, scope.GetEnvironmentResource<RecordingEnvironmentResource>(
                "dependency"));
            Assert.Equal(scope.ScenarioId, resource.ScenarioId);
            Assert.Equal(
                resource.Endpoint,
                scope.Services.GetRequiredService<IConfiguration>()["Dependency:Endpoint"]);
            Assert.Same(resource, scope.Services.GetRequiredService<RecordingEnvironmentResource>());
            Assert.Equal("started", events[0]);
            Assert.Contains("configuration", events);
            Assert.Contains("services", events);
            Assert.DoesNotContain("disposed", events);
        }

        Assert.Equal("disposed", events[^1]);
    }

    [Fact]
    public async Task Environment_resource_diagnostics_are_captured_before_cleanup()
    {
        using var factory = new AuthenticatedWebApplicationFactory<Program>();
        var resource = new RecordingEnvironmentResource("diagnostic-scenario", []);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.RunInTestScenarioScopeAsync(
                (_, _) => throw new InvalidOperationException("Expected test failure."),
                scope => scope.UseEnvironmentResource("dependency", resource),
                TestContext.Current.CancellationToken));

        var diagnostics = Assert.IsType<TestScenarioDiagnostics>(
            exception.Data[TestScenarioDiagnostics.ExceptionDataKey]);
        var serialized = JsonSerializer.Serialize(diagnostics);
        Assert.Contains("Environment", serialized);
        Assert.Contains("dependency", serialized);
        Assert.Contains(resource.Endpoint, serialized);
        Assert.True(resource.IsDisposed);
    }

    [Fact]
    public async Task Environment_resource_is_disposed_when_startup_fails()
    {
        using var factory = new AuthenticatedWebApplicationFactory<Program>();
        var events = new List<string>();
        var first = new RecordingEnvironmentResource("first-scenario", events);
        var failing = new RecordingEnvironmentResource(
            "failed-scenario",
            events,
            failOnStart: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.CreateTestScenarioScopeAsync(
                scope => scope
                    .UseEnvironmentResource("first", first)
                    .UseEnvironmentResource("failing", failing),
                TestContext.Current.CancellationToken));

        Assert.Equal("Expected startup failure.", exception.Message);
        Assert.True(first.IsDisposed);
        Assert.True(failing.IsDisposed);
        Assert.Equal(
            ["started", "started", "disposed", "disposed"],
            events);

        await using var recoveredScope = await factory.CreateTestScenarioScopeAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(recoveredScope.ScenarioId);
    }

    private sealed class RecordingEnvironmentResource(
        string scenarioId,
        List<string> events,
        bool failOnStart = false) : ITestScenarioEnvironmentResource
    {
        public string ScenarioId { get; } = scenarioId;

        public string Endpoint { get; } = $"https://{Guid.NewGuid():N}.example.test";

        public bool IsDisposed { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("started");
            if (failOnStart)
            {
                throw new InvalidOperationException("Expected startup failure.");
            }

            return ValueTask.CompletedTask;
        }

        public void ConfigureConfiguration(IConfigurationBuilder configuration)
        {
            Assert.False(IsDisposed);
            events.Add("configuration");
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dependency:Endpoint"] = Endpoint
            });
        }

        public void ConfigureServices(IServiceCollection services)
        {
            Assert.False(IsDisposed);
            events.Add("services");
            services.AddSingleton(this);
        }

        public ValueTask<object?> CaptureDiagnosticsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<object?>(new { Endpoint, IsDisposed });
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            events.Add("disposed");
            return ValueTask.CompletedTask;
        }
    }
}
