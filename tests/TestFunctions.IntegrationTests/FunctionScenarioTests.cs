using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.AzureFunctions;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class FunctionScenarioTests
{
    #region docs-functions-scenario-resources
    [Fact]
    public async Task Scenario_resources_span_invocations_and_reset_between_tests()
    {
        var resource = new RecordingResource { Value = "stale" };
        await using var host = AzureFunctionTestHost.CreateBuilder()
            .AddFunction<RecordingFunction>()
            .ConfigureServices(services =>
            {
                services.AddSingleton(resource);
                services.AddScoped<InvocationDependency>();
            })
            .UseScenarioResource("Published state", resource)
            .Build();

        var scopedTest = new ExampleScopedTest(host, TestContext.Current.CancellationToken);
        await scopedTest.Run(async (scope, token) =>
        {
            Assert.Null(resource.Value);
            Assert.Same(resource, scope.GetResource<RecordingResource>("published STATE"));
            var scenario = new RecordingScenario(scope, "Published state").WithValue("arranged");
            await scenario.ArrangeAsync(token);

            var first = await scope.Host.InvokeAsync<RecordingFunction, string>(
                scope.Host.CreateContext("First", token), (function, _) => function.RunAsync());
            var second = await scope.Host.InvokeAsync<RecordingFunction, string>(
                scope.Host.CreateContext("Second", token), (function, _) => function.RunAsync());

            Assert.Equal("arranged", first.Result);
            Assert.Equal("arranged", second.Result);
            Assert.Equal(2, resource.Invocations.Count);
            Assert.NotSame(resource.Invocations[0], resource.Invocations[1]);
            Assert.All(resource.Invocations, dependency => Assert.True(dependency.Disposed));
        });

        Assert.Null(resource.Value);
        Assert.Empty(resource.Invocations);
        Assert.Equal(2, resource.ResetTokens.Count);
        await scopedTest.Run((scope, _) =>
        {
            Assert.Null(scope.GetResource<RecordingResource>("Published state").Value);
            return Task.CompletedTask;
        });
    }
    #endregion

    [Fact]
    public async Task Callback_assertion_failure_captures_resources_before_cleanup()
    {
        var resource = new RecordingResource();
        await using var host = Build(resource);
        var failure = new InvalidOperationException("Expected assertion failure.");
        AzureFunctionTestScenarioScope? observed = null;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunInTestScenarioScopeAsync((scope, _) =>
        {
            observed = scope;
            resource.Value = "before cleanup";
            scope.Context.OnCleanup(() => { resource.Value = "cleanup"; return ValueTask.CompletedTask; });
            throw failure;
        }, TestContext.Current.CancellationToken));

        Assert.Same(failure, exception);
        var diagnostics = Diagnostics(exception);
        Assert.Same(observed!.Diagnostics, diagnostics);
        Assert.Equal(observed.ScenarioId, diagnostics.ScenarioId);
        Assert.Contains("before cleanup", JsonSerializer.Serialize(diagnostics));
        Assert.Null(resource.Value);
        Assert.False(Assert.Single(resource.CaptureTokens).CanBeCanceled);
        Assert.False(resource.ResetTokens[^1].CanBeCanceled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invocation_failure_preserves_diagnostics_and_original_exception_during_disposal(bool cancellation)
    {
        var resource = new RecordingResource();
        var disposalFailure = new InvalidOperationException("Invocation disposal failed.");
        var dependency = new InvocationDependency { Failure = disposalFailure };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var host = AzureFunctionTestHost.CreateBuilder()
            .AddFunction<RecordingFunction>()
            .ConfigureServices(services => { services.AddSingleton(resource); services.AddScoped(_ => dependency); })
            .UseScenarioResource("Recorded", resource)
            .Build();
        Exception failure = cancellation ? new OperationCanceledException(stop.Token) : new InvalidOperationException("Function failed.");
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.RunInTestScenarioScopeAsync(async (scope, token) =>
        {
            var context = AzureFunctionTestHost.QueueTrigger().WithBody("order-42").Build()
                .ApplyTo(scope.Host.CreateContext("Failure", token));
            await scope.Host.InvokeAsync<RecordingFunction>(context, (_, _) =>
            {
                resource.Value = "recorded before disposal";
                resource.BeforeCapture = () => Assert.False(dependency.Disposed);
                if (cancellation)
                {
                    stop.Cancel();
                }

                throw failure;
            });
        }, stop.Token));

        Assert.Same(failure, exception);
        if (cancellation)
        {
            Assert.Equal(stop.Token, Assert.IsType<OperationCanceledException>(exception).CancellationToken);
        }

        var diagnostics = Diagnostics(exception);
        var serialized = JsonSerializer.Serialize(diagnostics);
        Assert.Contains("recorded before disposal", serialized);
        Assert.Contains("message", serialized);
        Assert.Contains("Failure", serialized);
        Assert.True(dependency.Disposed);
        Assert.Null(resource.Value);
        Assert.False(resource.ResetTokens[^1].CanBeCanceled);
        var cleanup = Assert.IsType<AggregateException>(exception.Data[CleanupKey]);
        Assert.Contains(disposalFailure, cleanup.Flatten().InnerExceptions);
    }

    [Fact]
    public async Task Middleware_failure_has_invocation_metadata_and_resource_diagnostics()
    {
        var resource = new RecordingResource();
        var failure = new InvalidOperationException("Middleware failed.");
        await using var host = AzureFunctionTestHost.CreateBuilder()
            .UseScenarioResource("Recorded", resource)
            .ConfigureServices(services => { services.AddSingleton(resource); services.AddScoped<InvocationDependency>(); })
            .AddFunction<RecordingFunction>()
            .UseMiddleware((_, _) => { resource.Value = "middleware state"; throw failure; })
            .Build();
        var context = host.CreateContext("MiddlewareFailure", TestContext.Current.CancellationToken);
        var called = false;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InvokeAsync<RecordingFunction>(context, (_, _) =>
        {
            called = true;
            return Task.CompletedTask;
        }));
        Assert.Same(failure, exception);
        Assert.False(called);
        var diagnostics = Diagnostics(exception);
        Assert.Equal(context.InvocationId, diagnostics.ScenarioId);
        Assert.Contains("middleware state", JsonSerializer.Serialize(diagnostics));
        Assert.True(diagnostics.Resources.ContainsKey("$Invocation"));
        Assert.Empty(resource.ResetTokens); // Direct invocations do not create an outer scenario.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Result_and_nonresult_runners_preserve_failure_when_all_cleanup_stages_fail(bool resultRunner)
    {
        var resource = new RecordingResource();
        var healthy = new RecordingResource();
        await using var host = AzureFunctionTestHost.CreateBuilder()
            .UseScenarioResource("Failure", resource)
            .UseScenarioResource("Healthy", healthy)
            .Build();
        var original = new InvalidOperationException("Test failed.");
        var resetFailure = new InvalidOperationException("Reset failed.");
        var cleanupFailure = new InvalidOperationException("Owned cleanup failed.");
        var order = new List<int>();
        void Arrange(AzureFunctionTestScenarioScope scope)
        {
            resource.ResetFailure = resetFailure;
            scope.Context.OnCleanup(() => { order.Add(1); return ValueTask.CompletedTask; });
            scope.Context.OnCleanup(() => { order.Add(2); throw cleanupFailure; });
            scope.Context.OnCleanup(() => { order.Add(3); return ValueTask.CompletedTask; });
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => resultRunner
            ? host.RunInTestScenarioScopeAsync<int>((scope, _) => { Arrange(scope); throw original; }, TestContext.Current.CancellationToken)
            : host.RunInTestScenarioScopeAsync((scope, _) => { Arrange(scope); throw original; }, TestContext.Current.CancellationToken));
        Assert.Same(original, exception);
        Assert.Equal([3, 2, 1], order);
        var cleanup = Assert.IsType<AggregateException>(exception.Data[CleanupKey]).Flatten();
        Assert.Contains(cleanupFailure, cleanup.InnerExceptions);
        Assert.Contains(cleanup.InnerExceptions, error => ReferenceEquals(error.InnerException, resetFailure));
        Assert.Equal(2, healthy.ResetTokens.Count);
        resource.ResetFailure = null;
        await using var recovered = await host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cleanup_failure_after_a_successful_test_is_aggregated_and_does_not_strand_the_gate()
    {
        var resource = new RecordingResource();
        await using var host = Build(resource);
        var failure = new InvalidOperationException("Cleanup failed.");
        var exception = await Assert.ThrowsAsync<AggregateException>(() => host.RunInTestScenarioScopeAsync((scope, _) =>
        {
            scope.Context.OnCleanup(() => throw failure);
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken));
        Assert.Contains(failure, exception.Flatten().InnerExceptions);
        await using var recovered = await host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Diagnostic_failure_is_recorded_without_replacing_the_test_failure()
    {
        var resource = new RecordingResource { CaptureFailure = new InvalidOperationException("Capture failed.") };
        var healthy = new RecordingResource { Value = "before reset" };
        await using var host = AzureFunctionTestHost.CreateBuilder()
            .UseScenarioResource("Failure", resource).UseScenarioResource("Healthy", healthy).Build();
        var original = new InvalidOperationException("Original failure.");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunInTestScenarioScopeAsync((_, _) =>
        {
            healthy.Value = "healthy diagnostics";
            throw original;
        }, TestContext.Current.CancellationToken));
        Assert.Same(original, exception);
        var diagnostics = Diagnostics(exception);
        Assert.Contains("Capture failed.", JsonSerializer.Serialize(diagnostics.Resources["Failure"]));
        Assert.Contains("healthy diagnostics", JsonSerializer.Serialize(diagnostics.Resources["Healthy"]));
        Assert.Null(healthy.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_initialization_resets_every_resource_and_releases_the_gate(bool cancellation)
    {
        var resource = new RecordingResource();
        var healthy = new RecordingResource();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var failure = new InvalidOperationException("Initial reset failed.");
        resource.BeforeReset = token =>
        {
            if (resource.ResetTokens.Count == 1)
            {
                if (cancellation)
                {
                    stop.Cancel();
                    token.ThrowIfCancellationRequested();
                }

                throw failure;
            }
        };
        await using var host = AzureFunctionTestHost.CreateBuilder()
            .UseScenarioResource("Failure", resource).UseScenarioResource("Healthy", healthy).Build();
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.CreateTestScenarioScopeAsync(stop.Token));
        if (cancellation)
        {
            Assert.Equal(stop.Token, Assert.IsAssignableFrom<OperationCanceledException>(exception).CancellationToken);
        }
        else
        {
            Assert.Same(failure, Assert.Single(Assert.IsType<AggregateException>(exception).InnerExceptions).InnerException);
        }

        Diagnostics(exception);
        Assert.Equal(2, healthy.ResetTokens.Count);
        Assert.False(healthy.ResetTokens[^1].CanBeCanceled);
        await using var recovered = await host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Scenarios_serialize_resource_use_and_cancellation_does_not_release_another_scope()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().Build();
        var first = await host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var canceled = host.CreateTestScenarioScopeAsync(stop.Token);
        Assert.False(canceled.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        var waiting = host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        await first.DisposeAsync();
        await first.DisposeAsync();
        await using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotEqual(first.ScenarioId, second.ScenarioId);
        Assert.Throws<ObjectDisposedException>(() => first.Host);
    }

    [Fact]
    public async Task Registered_resources_are_borrowed_and_registration_is_validated()
    {
        var resource = new RecordingResource();
        var builder = AzureFunctionTestHost.CreateBuilder();
        Assert.Same(builder, builder.UseScenarioResource("Recorded", resource));
        Assert.Throws<ArgumentException>(() => builder.UseScenarioResource("recorded", resource));
        Assert.Throws<ArgumentException>(() => builder.UseScenarioResource("$invocation", resource));
        Assert.Throws<ArgumentException>(() => builder.UseScenarioResource(" ", resource));
        Assert.Throws<ArgumentNullException>(() => builder.UseScenarioResource("Null", null!));
        var host = builder.Build();
        await using (var scope = await host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Throws<KeyNotFoundException>(() => scope.GetResource<RecordingResource>("missing"));
            Assert.Throws<InvalidOperationException>(() => scope.GetResource<OtherResource>("Recorded"));
            Assert.Throws<ArgumentException>(() => scope.GetResource<RecordingResource>(" "));
            Assert.Throws<InvalidOperationException>(() => host.Dispose());
            Assert.Throws<InvalidOperationException>(() => host.DisposeAsync());
        }

        await host.DisposeAsync();
        Assert.False(resource.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Canceled_direct_invocation_does_not_execute_and_has_diagnostics()
    {
        var resource = new RecordingResource();
        await using var host = Build(resource);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var context = host.CreateContext("Canceled", stop.Token);
        var originalServices = context.InstanceServices;
        var called = false;
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.InvokeAsync<RecordingFunction>(context, (_, _) =>
        {
            called = true;
            return Task.CompletedTask;
        }));
        Assert.Equal(stop.Token, exception.CancellationToken);
        Assert.False(called);
        Assert.Same(originalServices, context.InstanceServices);
        Assert.Equal(context.InvocationId, Diagnostics(exception).ScenarioId);
    }

    [Fact]
    public async Task Invocation_disposal_failure_after_success_gets_diagnostics_and_restores_context_services()
    {
        var resource = new RecordingResource();
        var failure = new InvalidOperationException("Dispose failed.");
        var dependency = new InvocationDependency { Failure = failure };
        await using var host = AzureFunctionTestHost.CreateBuilder()
            .AddFunction<RecordingFunction>()
            .ConfigureServices(services => { services.AddSingleton(resource); services.AddScoped(_ => dependency); })
            .UseScenarioResource("Recorded", resource)
            .Build();
        var context = host.CreateContext("SuccessfulFunction", TestContext.Current.CancellationToken);
        var originalServices = context.InstanceServices;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InvokeAsync<RecordingFunction>(context, (_, _) =>
        {
            resource.Value = "function completed";
            return Task.CompletedTask;
        }));
        Assert.Same(failure, exception);
        Assert.True(dependency.Disposed);
        Assert.Same(originalServices, context.InstanceServices);
        Assert.Contains("function completed", JsonSerializer.Serialize(Diagnostics(exception)));
    }

    [Fact]
    public async Task Invocation_diagnostics_remain_serializable_with_context_owned_http_bindings()
    {
        await using var host = AzureFunctionTestHost.CreateBuilder().AddFunction<EmptyFunction>().Build();
        var context = host.CreateContext("HttpFailure", TestContext.Current.CancellationToken);
        var request = host.HttpRequest("HttpInput", TestContext.Current.CancellationToken).WithTextBody("body").Build();
        using var body = request.Body;
        context.WithInputBinding("request", request, "httpTrigger");
        var failure = new InvalidOperationException("HTTP function failed.");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.InvokeAsync<EmptyFunction>(
            context, (_, _) => throw failure));
        Assert.Same(failure, exception);
        Assert.Contains("request", JsonSerializer.Serialize(Diagnostics(exception)));
    }

    [Fact]
    public async Task Builder_registration_changes_do_not_modify_a_built_hosts_resource_snapshot()
    {
        var first = new RecordingResource();
        var late = new RecordingResource();
        var builder = AzureFunctionTestHost.CreateBuilder().UseScenarioResource("First", first);
        await using var host = builder.Build();
        builder.UseScenarioResource("Late", late);
        await using (var scope = await host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Throws<KeyNotFoundException>(() => scope.GetResource<RecordingResource>("Late"));
        }

        Assert.Equal(2, first.ResetTokens.Count);
        Assert.Empty(late.ResetTokens);
    }

    [Fact]
    public async Task Domain_arrangement_is_guarded_and_scoped_test_results_are_returned_after_cleanup()
    {
        var resource = new RecordingResource();
        await using var host = Build(resource);
        var scoped = new ExampleScopedTest(host, TestContext.Current.CancellationToken);
        var result = await scoped.RunResult(async (scope, token) =>
        {
            Assert.Equal(TestContext.Current.CancellationToken, token);
            var scenario = new RecordingScenario(scope).WithValue("arranged");
            await scenario.ArrangeAsync(token);
            Assert.Throws<InvalidOperationException>(() => scenario.WithValue("changed"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ArrangeAsync(token));
            return resource.Value;
        });
        Assert.Equal("arranged", result);
        Assert.Null(resource.Value);
        Assert.Throws<ArgumentNullException>(() => new ExampleScopedTest(null!, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentNullException>(() => new RecordingScenario(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => scoped.Run(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => scoped.RunResult<string>(null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_canceled_arrangement_consumes_the_scenario(bool cancellation)
    {
        var resource = new RecordingResource();
        await using var host = Build(resource);
        await using var scope = await host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scenario = new RecordingScenario(scope) { ArrangementFailure = new InvalidOperationException("Arrange failed.") };
        if (cancellation)
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scenario.ArrangeAsync(stop.Token));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ArrangeAsync(stop.Token));
        }

        Assert.Throws<InvalidOperationException>(() => scenario.WithValue("changed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ArrangeAsync(TestContext.Current.CancellationToken));
    }

    private const string CleanupKey = "XBullet.EasyTesting.TestScenarioCleanupException";

    private static TestScenarioDiagnostics Diagnostics(Exception exception) =>
        Assert.IsType<TestScenarioDiagnostics>(exception.Data[TestScenarioDiagnostics.ExceptionDataKey]);

    private static AzureFunctionTestHost Build(RecordingResource resource) => AzureFunctionTestHost.CreateBuilder()
        .UseScenarioResource("Recorded", resource).Build();

    private sealed class ExampleScopedTest(AzureFunctionTestHost host, CancellationToken token) : FunctionScopedTest(host, token)
    {
        public Task Run(Func<AzureFunctionTestScenarioScope, CancellationToken, Task> test) => RunAsync(test);
        public Task<T> RunResult<T>(Func<AzureFunctionTestScenarioScope, CancellationToken, Task<T>> test) => RunAsync(test);
    }

    private sealed class RecordingScenario(AzureFunctionTestScenarioScope scope, string resourceName = "Recorded") : FunctionScenario(scope)
    {
        private string? _value;
        public Exception? ArrangementFailure { get; init; }

        public RecordingScenario WithValue(string value)
        {
            EnsureNotArranged();
            _value = value;
            return this;
        }

        protected override Task ArrangeCoreAsync(CancellationToken cancellationToken)
        {
            if (ArrangementFailure is not null)
            {
                throw ArrangementFailure;
            }

            Scope.GetResource<RecordingResource>(resourceName).Value = _value;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyFunction;

    private sealed class RecordingFunction(RecordingResource resource, InvocationDependency dependency)
    {
        public Task<string> RunAsync()
        {
            resource.Invocations.Add(dependency);
            return Task.FromResult(resource.Value!);
        }
    }

    private sealed class InvocationDependency : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public Exception? Failure { get; init; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return Failure is null ? ValueTask.CompletedTask : ValueTask.FromException(Failure);
        }
    }

    private sealed class RecordingResource : ITestScenarioResource, IDisposable
    {
        public string? Value { get; set; }
        public List<InvocationDependency> Invocations { get; } = [];
        public List<CancellationToken> ResetTokens { get; } = [];
        public List<CancellationToken> CaptureTokens { get; } = [];
        public Exception? ResetFailure { get; set; }
        public Exception? CaptureFailure { get; init; }
        public Action<CancellationToken>? BeforeReset { get; set; }
        public Action? BeforeCapture { get; set; }
        public bool Disposed { get; private set; }

        public ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            ResetTokens.Add(cancellationToken);
            BeforeReset?.Invoke(cancellationToken);
            if (ResetFailure is not null)
            {
                throw ResetFailure;
            }

            Value = null;
            Invocations.Clear();
            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
        {
            CaptureTokens.Add(cancellationToken);
            BeforeCapture?.Invoke();
            return CaptureFailure is null ? ValueTask.FromResult<object?>(new { Value }) : ValueTask.FromException<object?>(CaptureFailure);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class OtherResource : ITestScenarioResource
    {
        public ValueTask ResetAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<object?>(null);
    }
}
