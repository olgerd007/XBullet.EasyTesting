using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using XBullet.EasyTesting.AzureFunctions;
using Xunit;

namespace TestFunctions.IntegrationTests;

public sealed class AzureFunctionScopeTests
{
    [Fact]
    public async Task Each_invocation_uses_one_isolated_scope()
    {
        await using var host = CreateHost<RecordingMiddleware>();
        var recorder = host.GetRequiredService<InvocationRecorder>();
        var singleton = host.GetRequiredService<SingletonDependency>();
        var firstContext = host.CreateContext(
            "FirstInvocation",
            TestContext.Current.CancellationToken);
        var secondContext = host.CreateContext(
            "SecondInvocation",
            TestContext.Current.CancellationToken);

        await host.InvokeAsync<ScopedFunction>(firstContext, (function, context) =>
            function.RunAsync(context));
        await host.InvokeAsync<ScopedFunction>(secondContext, (function, context) =>
            function.RunAsync(context));

        var firstMiddleware = Assert.Single(
            recorder.Middleware,
            item => item.InvocationId == firstContext.InvocationId);
        var firstFunction = Assert.Single(
            recorder.Functions,
            item => item.InvocationId == firstContext.InvocationId);
        var secondMiddleware = Assert.Single(
            recorder.Middleware,
            item => item.InvocationId == secondContext.InvocationId);
        var secondFunction = Assert.Single(
            recorder.Functions,
            item => item.InvocationId == secondContext.InvocationId);

        Assert.Same(firstMiddleware.Scoped, firstFunction.Scoped);
        Assert.Same(secondMiddleware.Scoped, secondFunction.Scoped);
        Assert.Same(firstMiddleware.Scoped, firstMiddleware.ContextScoped);
        Assert.Same(secondMiddleware.Scoped, secondMiddleware.ContextScoped);
        Assert.Same(firstFunction.Scoped, firstFunction.ContextScoped);
        Assert.Same(secondFunction.Scoped, secondFunction.ContextScoped);
        Assert.NotSame(firstFunction.Scoped, secondFunction.Scoped);
        Assert.Null(firstFunction.InitialValue);
        Assert.Null(secondFunction.InitialValue);
        Assert.Same(singleton, firstMiddleware.Singleton);
        Assert.Same(singleton, firstFunction.Singleton);
        Assert.Same(singleton, secondMiddleware.Singleton);
        Assert.Same(singleton, secondFunction.Singleton);
        Assert.NotSame(firstMiddleware.Transient, firstFunction.Transient);
        Assert.NotSame(secondMiddleware.Transient, secondFunction.Transient);
        Assert.NotSame(firstFunction.Transient, secondFunction.Transient);
        Assert.True(firstFunction.Scoped.DisposedAsynchronously);
        Assert.True(secondFunction.Scoped.DisposedAsynchronously);
        AssertContextNoLongerUsesInvocationScope(firstContext, firstFunction, singleton);
        AssertContextNoLongerUsesInvocationScope(secondContext, secondFunction, singleton);
    }

    [Fact]
    public async Task Invocation_scope_is_disposed_when_function_throws()
    {
        await using var host = CreateHost<RecordingMiddleware>();
        var recorder = host.GetRequiredService<InvocationRecorder>();
        var singleton = host.GetRequiredService<SingletonDependency>();
        var context = host.CreateContext(
            "FailingFunction",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.InvokeAsync<ScopedFunction>(context, (function, testContext) =>
                function.FailAsync(testContext)));

        var observation = Assert.Single(recorder.Functions);
        Assert.True(observation.Scoped.DisposedAsynchronously);
        AssertContextNoLongerUsesInvocationScope(context, observation, singleton);
    }

    [Fact]
    public async Task Invocation_scope_is_disposed_when_middleware_throws()
    {
        await using var host = CreateHost<ThrowingMiddleware>();
        var recorder = host.GetRequiredService<InvocationRecorder>();
        var singleton = host.GetRequiredService<SingletonDependency>();
        var context = host.CreateContext(
            "FailingMiddleware",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.InvokeAsync<ScopedFunction>(context, (function, testContext) =>
                function.RunAsync(testContext)));

        var observation = Assert.Single(recorder.Middleware);
        Assert.Empty(recorder.Functions);
        Assert.True(observation.Scoped.DisposedAsynchronously);
        AssertContextNoLongerUsesInvocationScope(context, observation, singleton);
    }

    private static AzureFunctionTestHost CreateHost<TMiddleware>()
        where TMiddleware : class, IFunctionsWorkerMiddleware =>
        AzureFunctionTestHost.CreateBuilder()
            .AddFunction<ScopedFunction>()
            .UseMiddleware<TMiddleware>()
            .ConfigureServices(services =>
            {
                services.AddScoped<MutableScopedDependency>();
                services.AddSingleton<InvocationRecorder>();
                services.AddSingleton<SingletonDependency>();
                services.AddTransient<TransientDependency>();
            })
            .Build();

    private static void AssertContextNoLongerUsesInvocationScope(
        TestFunctionContext context,
        InvocationObservation observation,
        SingletonDependency singleton)
    {
        Assert.NotSame(observation.InvocationServices, context.InstanceServices);
        Assert.Same(singleton, context.InstanceServices.GetRequiredService<SingletonDependency>());
        Assert.Throws<ObjectDisposedException>(() =>
            observation.InvocationServices.GetRequiredService<MutableScopedDependency>());
    }

    private sealed class ScopedFunction
    {
        private readonly MutableScopedDependency _scoped;
        private readonly SingletonDependency _singleton;
        private readonly TransientDependency _transient;
        private readonly InvocationRecorder _recorder;

        public ScopedFunction(
            MutableScopedDependency scoped,
            SingletonDependency singleton,
            TransientDependency transient,
            InvocationRecorder recorder)
        {
            _scoped = scoped;
            _singleton = singleton;
            _transient = transient;
            _recorder = recorder;
        }

        public Task RunAsync(TestFunctionContext context)
        {
            Record(context);
            return Task.CompletedTask;
        }

        public Task FailAsync(TestFunctionContext context)
        {
            Record(context);
            return Task.FromException(new InvalidOperationException("Function failed."));
        }

        private void Record(TestFunctionContext context)
        {
            var initialValue = _scoped.Value;
            _scoped.Value = context.InvocationId;
            _recorder.Functions.Add(new InvocationObservation(
                context.InvocationId,
                _scoped,
                context.InstanceServices.GetRequiredService<MutableScopedDependency>(),
                initialValue,
                _singleton,
                _transient,
                context.InstanceServices));
        }
    }

    private sealed class RecordingMiddleware
        : IFunctionsWorkerMiddleware
    {
        private readonly MutableScopedDependency _scoped;
        private readonly SingletonDependency _singleton;
        private readonly TransientDependency _transient;
        private readonly InvocationRecorder _recorder;

        public RecordingMiddleware(
            MutableScopedDependency scoped,
            SingletonDependency singleton,
            TransientDependency transient,
            InvocationRecorder recorder)
        {
            _scoped = scoped;
            _singleton = singleton;
            _transient = transient;
            _recorder = recorder;
        }

        public Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            Record(context);
            return next(context);
        }

        private void Record(FunctionContext context) =>
            _recorder.Middleware.Add(new InvocationObservation(
                context.InvocationId,
                _scoped,
                context.InstanceServices.GetRequiredService<MutableScopedDependency>(),
                _scoped.Value,
                _singleton,
                _transient,
                context.InstanceServices));
    }

    private sealed class ThrowingMiddleware
        : IFunctionsWorkerMiddleware
    {
        private readonly MutableScopedDependency _scoped;
        private readonly SingletonDependency _singleton;
        private readonly TransientDependency _transient;
        private readonly InvocationRecorder _recorder;

        public ThrowingMiddleware(
            MutableScopedDependency scoped,
            SingletonDependency singleton,
            TransientDependency transient,
            InvocationRecorder recorder)
        {
            _scoped = scoped;
            _singleton = singleton;
            _transient = transient;
            _recorder = recorder;
        }

        public Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            _recorder.Middleware.Add(new InvocationObservation(
                context.InvocationId,
                _scoped,
                context.InstanceServices.GetRequiredService<MutableScopedDependency>(),
                _scoped.Value,
                _singleton,
                _transient,
                context.InstanceServices));
            return Task.FromException(new InvalidOperationException("Middleware failed."));
        }
    }

    private sealed class MutableScopedDependency : IAsyncDisposable
    {
        public string? Value { get; set; }

        public bool DisposedAsynchronously { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposedAsynchronously = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SingletonDependency;

    private sealed class TransientDependency;

    private sealed class InvocationRecorder
    {
        public List<InvocationObservation> Middleware { get; } = [];

        public List<InvocationObservation> Functions { get; } = [];
    }

    private sealed record InvocationObservation(
        string InvocationId,
        MutableScopedDependency Scoped,
        MutableScopedDependency ContextScoped,
        string? InitialValue,
        SingletonDependency Singleton,
        TransientDependency Transient,
        IServiceProvider InvocationServices);
}
