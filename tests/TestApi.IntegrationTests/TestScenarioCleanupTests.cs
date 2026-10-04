using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Messaging;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class TestScenarioCleanupTests
{
    [Fact]
    public async Task Disposal_attempts_every_stage_aggregates_failures_and_releases_the_gate()
    {
        var events = new List<string>();
        var factoryFailure = new InvalidOperationException("Factory cleanup failed.");
        var hostFailure = new InvalidOperationException("Host disposal failed.");
        var callbackFailure = new InvalidOperationException("Cleanup callback failed.");
        var resourceFailure = new InvalidOperationException("Owned resource disposal failed.");
        var resetFailure = new InvalidOperationException("Recorder reset failed.");
        var failingReset = new ResetResource("failing", events);
        var healthyReset = new ResetResource("healthy", events);
        var synchronous = new SyncResource(events, resourceFailure);
        var asynchronous = new AsyncResource(events);
        var host = new HostResource(events, hostFailure);
        using var factory = new CleanupFactory(("failing", failingReset), ("healthy", healthyReset))
        {
            Configure = (services, context) =>
            {
                services.AddSingleton(_ => host);
                context.DisposeWithScenario(synchronous);
                context.DisposeWithScenario(asynchronous);
                context.OnCleanup(() =>
                {
                    events.Add("callback");
                    throw callbackFailure;
                });
            },
            Cleanup = (_, token) =>
            {
                Assert.False(token.CanBeCanceled);
                events.Add("factory");
                throw factoryFailure;
            }
        };
        await using var scope = await factory.CreateTestScenarioScopeAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Same(host, scope.Services.GetRequiredService<HostResource>());
        factory.PublishedMessages.Record("test", "before-cleanup", new { Value = 42 });
        failingReset.Failure = resetFailure;
        events.Clear();

        var exception = await Assert.ThrowsAsync<AggregateException>(() => scope.DisposeAsync().AsTask());
        Assert.Equal(
            ["factory", "host", "callback", "async", "sync", "reset:failing", "reset:healthy"],
            events);
        var failures = exception.Flatten().InnerExceptions;
        Assert.Equal(5, failures.Count);
        Assert.Contains(factoryFailure, failures);
        Assert.Contains(hostFailure, failures);
        Assert.Contains(callbackFailure, failures);
        Assert.Contains(resourceFailure, failures);
        var resetException = Assert.Single(failures, failure => ReferenceEquals(failure.InnerException, resetFailure));
        Assert.Contains("'failing'", resetException.Message);
        Assert.Equal(0, factory.PublishedMessages.Count);
        Assert.Equal(2, healthyReset.ResetTokens.Count);
        Assert.False(failingReset.ResetTokens[^1].CanBeCanceled);
        Assert.False(healthyReset.ResetTokens[^1].CanBeCanceled);

        await scope.DisposeAsync();
        Assert.Equal(1, synchronous.DisposeCount);
        Assert.Equal(1, asynchronous.DisposeCount);
        Assert.Equal(1, host.DisposeCount);
        Assert.Equal(7, events.Count);

        failingReset.Failure = null;
        factory.Configure = null;
        factory.Cleanup = null;
        await AssertFactoryCanRecoverAsync(factory, scope.ScenarioId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Runner_preserves_test_failure_or_cancellation_when_cleanup_also_fails(
        bool returnsResult,
        bool cancelTest)
    {
        var events = new List<string>();
        var reset = new ResetResource("recorder", events);
        var cleanupFailure = new InvalidOperationException("Factory cleanup failed.");
        var resourceFailure = new InvalidOperationException("Resource cleanup failed.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        Exception originalFailure = cancelTest
            ? new OperationCanceledException("Test canceled.", cancellation.Token)
            : new InvalidOperationException("Test failed.");
        using var factory = new CleanupFactory(("recorder", reset))
        {
            Configure = (_, context) => context.OnCleanup(() =>
            {
                events.Add("resource");
                throw resourceFailure;
            }),
            Cleanup = (_, token) =>
            {
                Assert.False(token.CanBeCanceled);
                events.Add("factory");
                throw cleanupFailure;
            }
        };
        string? failedScenarioId = null;

        Task FailTest(TestScenarioScope<CleanupStartup> scope, CancellationToken token)
        {
            Assert.Equal(cancellation.Token, token);
            failedScenarioId = scope.ScenarioId;
            events.Clear();
            events.Add("test");
            factory.PublishedMessages.Record("test", "before-failure", new { Value = 42 });
            if (cancelTest)
            {
                cancellation.Cancel();
            }

            return Task.FromException(originalFailure);
        }

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => returnsResult
            ? factory.RunInTestScenarioScopeAsync<int>(async (scope, token) =>
                {
                    await FailTest(scope, token);
                    return 42;
                }, cancellationToken: cancellation.Token)
            : factory.RunInTestScenarioScopeAsync(FailTest, cancellationToken: cancellation.Token));

        Assert.Same(originalFailure, thrown);
        var diagnostics = Assert.IsType<TestScenarioDiagnostics>(
            thrown.Data[TestScenarioDiagnostics.ExceptionDataKey]);
        Assert.Equal(failedScenarioId, diagnostics.ScenarioId);
        Assert.Contains("before-failure", JsonSerializer.Serialize(diagnostics));
        var cleanup = Assert.IsType<AggregateException>(
            thrown.Data["XBullet.EasyTesting.TestScenarioCleanupException"]);
        Assert.Equal(2, cleanup.Flatten().InnerExceptions.Count);
        Assert.Contains(cleanupFailure, cleanup.Flatten().InnerExceptions);
        Assert.Contains(resourceFailure, cleanup.Flatten().InnerExceptions);
        Assert.Equal(["test", "factory", "resource", "reset:recorder"], events);
        Assert.False(reset.DiagnosticTokens.Single().CanBeCanceled);
        Assert.False(reset.ResetTokens[^1].CanBeCanceled);
        Assert.Equal(0, factory.PublishedMessages.Count);
        if (cancelTest)
        {
            Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(thrown).CancellationToken);
        }

        factory.Configure = null;
        factory.Cleanup = null;
        await AssertFactoryCanRecoverAsync(factory, failedScenarioId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initialization_failure_or_cancellation_is_preserved_when_cleanup_fails(bool cancelStartup)
    {
        var events = new List<string>();
        var reset = new ResetResource("recorder", events);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        Exception originalFailure = cancelStartup
            ? new OperationCanceledException("Initialization canceled.", cancellation.Token)
            : new InvalidOperationException("Initialization failed.");
        using var factory = new CleanupFactory(("recorder", reset))
        {
            Configure = (_, context) => context.OnCleanup(() =>
            {
                events.Add("resource");
                throw new InvalidOperationException("Resource cleanup failed.");
            }),
            Initialize = (_, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                events.Add("initialize");
                if (cancelStartup)
                {
                    cancellation.Cancel();
                }

                return Task.FromException(originalFailure);
            },
            Cleanup = (_, token) =>
            {
                Assert.False(token.CanBeCanceled);
                events.Add("factory");
                throw new InvalidOperationException("Factory cleanup failed.");
            }
        };

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            factory.CreateTestScenarioScopeAsync(cancellationToken: cancellation.Token));

        Assert.Same(originalFailure, thrown);
        Assert.Equal(["reset:recorder", "initialize", "factory", "resource", "reset:recorder"], events);
        Assert.False(reset.ResetTokens[^1].CanBeCanceled);
        factory.Configure = null;
        factory.Initialize = null;
        factory.Cleanup = null;
        await AssertFactoryCanRecoverAsync(factory);
    }

    [Fact]
    public async Task Environment_startup_cancellation_cleans_resources_in_reverse_order_despite_disposal_failure()
    {
        var events = new List<string>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var originalFailure = new OperationCanceledException("Resource startup canceled.", cancellation.Token);
        var first = new EnvironmentResource("first", events,
            disposeFailure: new InvalidOperationException("First resource disposal failed."));
        var second = new EnvironmentResource("second", events, token =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            throw originalFailure;
        });
        using var factory = TestApiHostSettings.CreateIsolatedFactory();

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            factory.CreateTestScenarioScopeAsync(scope => scope
                .UseEnvironmentResource("first", first)
                .UseEnvironmentResource("second", second), cancellation.Token));

        Assert.Same(originalFailure, thrown);
        Assert.Equal(["start:first", "start:second", "dispose:second", "dispose:first"], events);
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        await AssertFactoryCanRecoverAsync(factory);
    }

    private static async Task AssertFactoryCanRecoverAsync<TEntryPoint>(
        AuthenticatedWebApplicationFactory<TEntryPoint> factory,
        string? previousScenarioId = null)
        where TEntryPoint : class
    {
        // Bound the gate wait so a cleanup regression fails instead of hanging the test suite.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var recovered = await factory.CreateTestScenarioScopeAsync(cancellationToken: timeout.Token);
        Assert.NotEqual(previousScenarioId, recovered.ScenarioId);
        using var client = recovered.CreateAnonymousClient();
        using var response = await client.GetAsync("/health", timeout.Token);
        response.EnsureSuccessStatusCode();
    }

    public sealed class CleanupStartup
    {
        public void Configure(IApplicationBuilder application) =>
            application.Run(context => context.Response.WriteAsync("Healthy"));
    }

    private sealed class CleanupFactory : StartupAuthenticatedWebApplicationFactory<CleanupStartup>
    {
        public CleanupFactory(params (string Name, ITestScenarioResource Resource)[] resources)
        {
            RegisterScenarioResource("Published messages", PublishedMessages);
            foreach (var resource in resources)
            {
                RegisterScenarioResource(resource.Name, resource.Resource);
            }
        }

        public Action<IServiceCollection, TestScenarioContext>? Configure { get; set; }

        public RecordedMessageBus PublishedMessages { get; } = new();

        public Func<TestScenarioScope<CleanupStartup>, CancellationToken, Task>? Initialize { get; set; }

        public Func<TestScenarioScope<CleanupStartup>, CancellationToken, Task>? Cleanup { get; set; }

        protected override void ConfigureServicesForScenario(IServiceCollection services, TestScenarioContext context)
        {
            base.ConfigureServicesForScenario(services, context);
            Configure?.Invoke(services, context);
        }

        protected override async Task InitializeScenarioAsync(
            TestScenarioScope<CleanupStartup> scope,
            CancellationToken cancellationToken)
        {
            await base.InitializeScenarioAsync(scope, cancellationToken);
            if (Initialize is not null)
            {
                await Initialize(scope, cancellationToken);
            }
        }

        protected override async Task CleanupScenarioAsync(
            TestScenarioScope<CleanupStartup> scope,
            CancellationToken cancellationToken)
        {
            await base.CleanupScenarioAsync(scope, cancellationToken);
            if (Cleanup is not null)
            {
                await Cleanup(scope, cancellationToken);
            }
        }
    }

    private sealed class ResetResource(string name, List<string> events) : ITestScenarioResource
    {
        public Exception? Failure { get; set; }

        public List<CancellationToken> ResetTokens { get; } = [];

        public List<CancellationToken> DiagnosticTokens { get; } = [];

        public ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetTokens.Add(cancellationToken);
            events.Add($"reset:{name}");
            if (Failure is not null)
            {
                throw Failure;
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiagnosticTokens.Add(cancellationToken);
            return ValueTask.FromResult<object?>(new { ResetCount = ResetTokens.Count });
        }
    }

    private sealed class SyncResource(List<string> events, Exception failure) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            events.Add("sync");
            throw failure;
        }
    }

    private sealed class AsyncResource(List<string> events) : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            events.Add("async");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HostResource(List<string> events, Exception failure) : IDisposable, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            if (DisposeCount > 0)
            {
                return;
            }

            DisposeCount++;
            events.Add("host");
            throw failure;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EnvironmentResource(
        string name,
        List<string> events,
        Action<CancellationToken>? start = null,
        Exception? disposeFailure = null) : ITestScenarioEnvironmentResource
    {
        public int DisposeCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            events.Add($"start:{name}");
            start?.Invoke(cancellationToken);
            return ValueTask.CompletedTask;
        }

        public void ConfigureConfiguration(IConfigurationBuilder configuration)
        {
        }

        public void ConfigureServices(IServiceCollection services)
        {
        }

        public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<object?>(null);

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            events.Add($"dispose:{name}");
            if (disposeFailure is not null)
            {
                throw disposeFailure;
            }

            return ValueTask.CompletedTask;
        }
    }
}
