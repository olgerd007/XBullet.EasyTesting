using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Provides function instances and fluent trigger data backed by one service provider.</summary>
public sealed class AzureFunctionTestHost : IDisposable, IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly IReadOnlyList<Func<IServiceProvider, IFunctionsWorkerMiddleware>> _middleware;

    internal AzureFunctionTestHost(
        ServiceProvider services,
        IReadOnlyList<Func<IServiceProvider, IFunctionsWorkerMiddleware>> middleware)
    {
        _services = services;
        _middleware = middleware;
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

    /// <summary>Starts a fluent Service Bus trigger definition.</summary>
    public static ServiceBusTriggerBuilder ServiceBusTrigger() => new();

    /// <summary>Starts a fluent Queue Storage trigger definition.</summary>
    public static QueueTriggerBuilder QueueTrigger() => new();

    /// <summary>Starts a fluent Blob Storage trigger definition.</summary>
    public static BlobTriggerBuilder BlobTrigger() => new();

    /// <summary>Starts a fluent Event Grid trigger definition.</summary>
    public static EventGridTriggerBuilder EventGridTrigger() => new();

    /// <summary>Starts a fluent Event Hubs trigger definition.</summary>
    public static EventHubsTriggerBuilder EventHubsTrigger() => new();

    /// <summary>Executes a function delegate through configured worker middleware.</summary>
    public async Task<TestFunctionInvocationResult> InvokeAsync<TFunction>(
        TestFunctionContext context,
        Func<TFunction, TestFunctionContext, Task> invoke)
        where TFunction : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoke);
        context.FunctionDefinition.ConfigureForFunction(typeof(TFunction));
        var function = _services.GetRequiredService<TFunction>();
        var functionExecuted = false;

        FunctionExecutionDelegate terminal = async workerContext =>
        {
            functionExecuted = true;
            await invoke(function, RequireTestContext(workerContext)).ConfigureAwait(false);
        };

        await BuildPipeline(terminal)(context).ConfigureAwait(false);
        return new TestFunctionInvocationResult(context, functionExecuted);
    }

    /// <summary>Executes a value-returning function delegate and captures all returned output properties.</summary>
    public async Task<TestFunctionInvocationResult<TResult>> InvokeAsync<TFunction, TResult>(
        TestFunctionContext context,
        Func<TFunction, TestFunctionContext, Task<TResult>> invoke)
        where TFunction : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoke);
        context.FunctionDefinition.ConfigureForFunction(typeof(TFunction));
        var function = _services.GetRequiredService<TFunction>();
        var functionExecuted = false;
        TResult? result = default;

        FunctionExecutionDelegate terminal = async workerContext =>
        {
            functionExecuted = true;
            result = await invoke(function, RequireTestContext(workerContext)).ConfigureAwait(false);
            context.Bindings.CaptureInvocationResult(result);
            foreach (var output in context.Bindings.Outputs.Keys)
            {
                context.FunctionDefinition.AddOutput(
                    output,
                    context.Bindings.OutputTypes[output]);
            }
        };

        await BuildPipeline(terminal)(context).ConfigureAwait(false);
        return new TestFunctionInvocationResult<TResult>(context, functionExecuted, result);
    }

    /// <summary>Creates a context, captures trigger input, and invokes a function through middleware.</summary>
    public Task<TestFunctionInvocationResult> InvokeAsync<TFunction, TTrigger>(
        string functionName,
        TestTriggerData<TTrigger> trigger,
        Func<TFunction, TTrigger, TestFunctionContext, Task> invoke,
        CancellationToken cancellationToken = default)
        where TFunction : notnull
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(invoke);
        var context = trigger.ApplyTo(CreateContext(functionName, cancellationToken));
        return InvokeAsync<TFunction>(context, (function, testContext) =>
            invoke(function, trigger.Value, testContext));
    }

    /// <summary>Creates a context, captures trigger input and returned outputs, and invokes a function through middleware.</summary>
    public Task<TestFunctionInvocationResult<TResult>> InvokeAsync<TFunction, TTrigger, TResult>(
        string functionName,
        TestTriggerData<TTrigger> trigger,
        Func<TFunction, TTrigger, TestFunctionContext, Task<TResult>> invoke,
        CancellationToken cancellationToken = default)
        where TFunction : notnull
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(invoke);
        var context = trigger.ApplyTo(CreateContext(functionName, cancellationToken));
        return InvokeAsync<TFunction, TResult>(context, (function, testContext) =>
            invoke(function, trigger.Value, testContext));
    }

    private FunctionExecutionDelegate BuildPipeline(FunctionExecutionDelegate terminal)
    {
        var next = terminal;
        for (var index = _middleware.Count - 1; index >= 0; index--)
        {
            var middleware = _middleware[index](_services);
            var capturedNext = next;
            next = context => middleware.Invoke(context, capturedNext);
        }

        return next;
    }

    private static TestFunctionContext RequireTestContext(FunctionContext context) =>
        context as TestFunctionContext
        ?? throw new InvalidOperationException(
            $"Expected {nameof(TestFunctionContext)}, but middleware supplied {context.GetType().FullName}.");

    /// <inheritdoc />
    public void Dispose() => _services.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
