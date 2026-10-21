using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TestFunctions.External;
using TestFunctions.Functions;
using TestFunctions.Models;
using TestFunctions.Services;
using XBullet.EasyTesting.AzureFunctions;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Http;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class AzureFunctionTestFactoryTests
{
    #region docs-functions-program-factory
    [Fact]
    public async Task Factory_reuses_program_configuration_and_services_with_test_overrides()
    {
        await using var factory = new OrderFunctionFactory();
        var host = await factory.CreateHostAsync(TestContext.Current.CancellationToken);
        factory.Pricing.When(HttpMethod.Get, "/products/42/price")
            .RespondJson(new ProductPrice(42, 19.95m, "USD"));

        await host.RunInTestScenarioScopeAsync(async (scope, token) =>
        {
            var trigger = KafkaTriggerData.JsonTrigger(
                new OrderPricingRequestedMessage("order-42", 42, 3), topic: "order-pricing");
            await scope.Host.InvokeAsync<PriceOrderKafkaFunction, string>(
                nameof(PriceOrderKafkaFunction), trigger,
                (function, message, context) => function.RunAsync(message, context), token);

            Assert.Equal(59.85m, Assert.Single(factory.Recorder.Invocations).Amount);
            factory.Pricing.VerifyCalled(HttpMethod.Get, "/products/42/price");
        }, TestContext.Current.CancellationToken);
    }
    #endregion

    #region docs-functions-program-factory-definition
    private sealed class OrderFunctionFactory : AzureFunctionTestFactory<Program>
    {
        public StubHttpMessageHandler Pricing { get; } = new();
        public RecordingResource Recorder { get; } = new();

        protected override void ConfigureHost(IHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["OrderPricing:BaseUrl"] = "https://factory-pricing.test/" }));

        protected override void ConfigureTestHost(AzureFunctionTestHostBuilder builder) =>
            builder.UseScenarioResource("Recorded invocations", Recorder);

        protected override void ConfigureTestServices(IServiceCollection services)
        {
            services.RemoveAll<ITriggerInvocationSink>();
            services.AddSingleton<ITriggerInvocationSink>(Recorder);
            // Keep the typed client registration and base address configured by Program.cs.
            services.AddHttpClient<IOrderPricingClient, OrderPricingClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Pricing)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        }
    }
    #endregion

    [Fact]
    public async Task Unmodified_program_services_are_available_and_the_worker_has_not_started()
    {
        await using var factory = new AzureFunctionTestFactory<Program>();
        var host = await factory.CreateHostAsync(TestContext.Current.CancellationToken);
        Assert.IsType<LoggingTriggerInvocationSink>(host.GetRequiredService<ITriggerInvocationSink>());
        Assert.IsType<OrderPricingClient>(host.GetRequiredService<IOrderPricingClient>());
        Assert.IsType<ProcessOrderHttpFunction>(host.GetRequiredService<ProcessOrderHttpFunction>());
        Assert.False(host.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested);
        Assert.NotNull(host.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.Azure.Functions.Worker.WorkerOptions>>().Value.Serializer);
    }

    [Fact]
    public async Task Factory_host_is_cached_and_production_configuration_reaches_the_original_http_client()
    {
        await using var factory = new OrderFunctionFactory();
        var hosts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => factory.CreateHostAsync(TestContext.Current.CancellationToken)));
        Assert.All(hosts, host => Assert.Same(hosts[0], host));
        factory.Pricing.When(HttpMethod.Get, "/products/42/price").RespondJson(new ProductPrice(42, 19.95m, "USD"));
        var price = await hosts[0].GetRequiredService<IOrderPricingClient>().GetProductPriceAsync(42, TestContext.Current.CancellationToken);
        Assert.Equal(19.95m, price!.UnitPrice);
        Assert.Equal("factory-pricing.test", Assert.Single(factory.Pricing.Requests).RequestUri!.Host);
    }

    [Fact]
    public async Task Factories_build_distinct_containers_and_do_not_capture_each_others_hosts()
    {
        await using var first = new OrderFunctionFactory();
        await using var second = new OrderFunctionFactory();
        var hosts = await Task.WhenAll(first.CreateHostAsync(TestContext.Current.CancellationToken), second.CreateHostAsync(TestContext.Current.CancellationToken));
        Assert.NotSame(hosts[0], hosts[1]);
        Assert.Same(first.Recorder, hosts[0].GetRequiredService<ITriggerInvocationSink>());
        Assert.Same(second.Recorder, hosts[1].GetRequiredService<ITriggerInvocationSink>());
    }

    [Fact]
    public async Task Invocation_middleware_and_scenario_diagnostics_work_on_the_production_provider()
    {
        await using var factory = new MiddlewareFactory();
        var host = await factory.CreateHostAsync(TestContext.Current.CancellationToken);
        var failure = new InvalidOperationException("Test assertion failed.");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunInTestScenarioScopeAsync(async (scope, token) =>
        {
            await scope.Host.InvokeAsync<ProcessOrderKafkaFunction, string>(
                nameof(ProcessOrderKafkaFunction), KafkaTriggerData.JsonTrigger(new KafkaOrderMessage("order-42", 1), topic: "orders"),
                (function, message, context) => function.RunAsync(message, context), token);
            Assert.Equal("from test middleware", factory.Recorder.Invocations.Single().Subject);
            throw failure;
        }, TestContext.Current.CancellationToken));
        Assert.Same(failure, exception);
        Assert.Contains("from test middleware", JsonSerializer.Serialize(exception.Data[TestScenarioDiagnostics.ExceptionDataKey]));
        Assert.Empty(factory.Recorder.Invocations);
    }

    [Fact]
    public async Task Initialization_errors_keep_their_identity_and_can_be_retried()
    {
        await using var factory = new FailingFactory();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateHostAsync(TestContext.Current.CancellationToken));
        Assert.Same(factory.Failure, exception);
        var host = await factory.CreateHostAsync(TestContext.Current.CancellationToken);
        Assert.IsType<ProcessOrderHttpFunction>(host.GetRequiredService<ProcessOrderHttpFunction>());
    }

    [Fact]
    public async Task Factory_owns_the_host_and_async_services_and_rejects_use_after_disposal()
    {
        var factory = new OwnershipFactory();
        var host = await factory.CreateHostAsync(TestContext.Current.CancellationToken);
        var owned = host.GetRequiredService<OwnedService>();
        var scope = await host.CreateTestScenarioScopeAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.DisposeAsync().AsTask());
        Assert.Equal(0, owned.DisposeCount);
        Assert.Same(host, await factory.CreateHostAsync(TestContext.Current.CancellationToken));
        Assert.Same(owned, host.GetRequiredService<OwnedService>());
        await scope.DisposeAsync();
        await factory.DisposeAsync();
        await factory.DisposeAsync();
        Assert.Equal(1, owned.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => factory.CreateHostAsync(TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() => host.GetRequiredService<OwnedService>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factory_rejects_use_after_service_disposal_fails(bool throwSynchronously)
    {
        await using var factory = new DisposalFailureFactory(throwSynchronously);
        var host = await factory.CreateHostAsync(TestContext.Current.CancellationToken);
        var owned = host.GetRequiredService<ThrowingOwnedService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.DisposeAsync().AsTask());

        Assert.Same(owned.Failure, exception);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => factory.CreateHostAsync(TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(() => host.GetRequiredService<ThrowingOwnedService>());
        await factory.DisposeAsync();
        Assert.Equal(1, owned.DisposeCount);
    }

    [Fact]
    public async Task Nonexecutable_entry_point_and_invalid_arguments_are_rejected()
    {
        await using var libraryFactory = new AzureFunctionTestFactory<AzureFunctionTestHost>();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => libraryFactory.CreateHostAsync(TestContext.Current.CancellationToken));
        Assert.Contains("no entry point", exception.Message);
        await using var nullArguments = new ArgumentsFactory(null!);
        await Assert.ThrowsAsync<ArgumentNullException>(() => nullArguments.CreateHostAsync(TestContext.Current.CancellationToken));
        await using var nullElement = new ArgumentsFactory([null!]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => nullElement.CreateHostAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancellation_before_initialization_preserves_the_callers_token()
    {
        await using var factory = new AzureFunctionTestFactory<Program>();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.CreateHostAsync(stop.Token));
        Assert.Equal(stop.Token, exception.CancellationToken);
        var host = await factory.CreateHostAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(host);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_supports_hostbuilder_and_hostapplicationbuilder_without_running_the_host(bool modern)
    {
        var reachedRun = false;
        var marker = new object();
        var host = await FunctionProgramHostCapture.CaptureAsync(() =>
        {
            using var application = modern ? Host.CreateApplicationBuilder().Build() : new HostBuilder().Build();
            reachedRun = true;
            return Task.CompletedTask;
        }, builder => builder.ConfigureServices((_, services) => services.AddSingleton(marker)),
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try
        {
            Assert.Same(marker, host.Services.GetRequiredService<object>());
            Assert.False(reachedRun);
        }
        finally
        {
            await ((IAsyncDisposable)host).DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Canceled_or_timed_out_startup_still_stops_and_disposes_a_late_built_host(bool cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new DisposalLoggerProvider();
        var reachedRun = false;
        var capture = FunctionProgramHostCapture.CaptureAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            using var host = new HostBuilder().ConfigureServices(services => services.AddSingleton<ILoggerProvider>(_ => provider)).Build();
            reachedRun = true;
        }, _ => { }, cancellation ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(25), stop.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            if (cancellation)
            {
                stop.Cancel();
                var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
                Assert.Equal(stop.Token, exception.CancellationToken);
            }
            else
            {
                await Assert.ThrowsAsync<TimeoutException>(() => capture);
            }
        }
        finally
        {
            release.TrySetResult();
        }

        await provider.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(reachedRun);
    }

    [Fact]
    public async Task Capture_reports_entrypoint_errors_and_programs_that_never_build_a_host()
    {
        var failure = new InvalidOperationException("Program failed.");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => FunctionProgramHostCapture.CaptureAsync(
            () => Task.FromException(failure), _ => { }, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Same(failure, exception);
        var noHost = await Assert.ThrowsAsync<InvalidOperationException>(() => FunctionProgramHostCapture.CaptureAsync(
            () => Task.CompletedTask, _ => { }, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Contains("without building an IHost", noHost.Message);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => FunctionProgramHostCapture.CaptureAsync(
            () => Task.CompletedTask, _ => { }, TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    private sealed class MiddlewareFactory : AzureFunctionTestFactory<Program>
    {
        public RecordingResource Recorder { get; } = new();

        protected override void ConfigureTestHost(AzureFunctionTestHostBuilder builder) => builder
            .UseScenarioResource("Invocations", Recorder)
            .UseMiddleware(async (context, next) =>
            {
                await Recorder.RecordAsync(new TriggerInvocation("middleware", "from test middleware", 1), context.CancellationToken);
                // Deliberately short-circuit so the default function's input parser does not run.
            });
    }

    private sealed class OwnershipFactory : AzureFunctionTestFactory<Program>
    {
        protected override void ConfigureTestServices(IServiceCollection services) => services.AddSingleton<OwnedService>();
    }

    private sealed class DisposalFailureFactory(bool throwSynchronously) : AzureFunctionTestFactory<Program>
    {
        protected override void ConfigureTestServices(IServiceCollection services) =>
            services.AddSingleton(_ => new ThrowingOwnedService(throwSynchronously));
    }

    private sealed class ThrowingOwnedService(bool throwSynchronously) : IAsyncDisposable
    {
        public InvalidOperationException Failure { get; } = new("Service cleanup failed.");
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (throwSynchronously)
            {
                throw Failure;
            }

            return ValueTask.FromException(Failure);
        }
    }

    private sealed class FailingFactory : AzureFunctionTestFactory<Program>
    {
        private bool _failed;
        public InvalidOperationException Failure { get; } = new("Override failed.");
        protected override void ConfigureTestServices(IServiceCollection services)
        {
            if (!_failed)
            {
                _failed = true;
                throw Failure;
            }
        }
    }

    private sealed class ArgumentsFactory(string[] arguments) : AzureFunctionTestFactory<Program>
    {
        protected override string[] GetArguments() => arguments;
    }

    private sealed class OwnedService : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }

    private sealed class DisposalLoggerProvider : ILoggerProvider
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() => Disposed.TrySetResult();
    }

    private sealed class RecordingResource : ITriggerInvocationSink, ITestScenarioResource
    {
        private readonly List<TriggerInvocation> _invocations = [];
        public IReadOnlyList<TriggerInvocation> Invocations => _invocations;
        public Task RecordAsync(TriggerInvocation invocation, CancellationToken cancellationToken = default)
        {
            _invocations.Add(invocation);
            return Task.CompletedTask;
        }
        public ValueTask ResetAsync(CancellationToken cancellationToken = default) { _invocations.Clear(); return ValueTask.CompletedTask; }
        public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<object?>(_invocations.ToArray());
    }
}
