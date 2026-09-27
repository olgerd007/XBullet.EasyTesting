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
    /// <returns>A new mutable builder with logging and web-default JSON worker serialization configured.</returns>
    public static AzureFunctionTestHostBuilder CreateBuilder() => new();

    /// <summary>Resolves a registered function or dependency.</summary>
    /// <typeparam name="T">The non-null service type to resolve from the host's root provider.</typeparam>
    /// <returns>The required registered service. The host retains disposal ownership.</returns>
    public T GetRequiredService<T>()
        where T : notnull =>
        _services.GetRequiredService<T>();

    /// <summary>Creates a minimal isolated-worker invocation context.</summary>
    /// <param name="functionName">The non-empty friendly name and identifier assigned to the function.</param>
    /// <param name="cancellationToken">
    /// The token exposed through the context. The default token does not request cancellation.
    /// </param>
    /// <returns>A new mutable context backed initially by the host's root services.</returns>
    public TestFunctionContext CreateContext(
        string functionName,
        CancellationToken cancellationToken = default) =>
        new(_services, functionName, cancellationToken);

    /// <summary>Starts a fluent HTTP-trigger request for a function.</summary>
    /// <param name="functionName">The non-empty friendly name assigned to the invocation context.</param>
    /// <param name="cancellationToken">
    /// The token exposed through the request's function context. The default does not request cancellation.
    /// </param>
    /// <returns>A new mutable HTTP request builder and invocation context.</returns>
    public TestHttpRequestBuilder HttpRequest(
        string functionName,
        CancellationToken cancellationToken = default) =>
        new(CreateContext(functionName, cancellationToken));

    /// <summary>Starts a fluent timer-trigger data definition.</summary>
    /// <returns>A new mutable timer builder with an on-time invocation and no schedule status.</returns>
    public static TestTimerInfoBuilder Timer() => new();

    /// <summary>Starts a fluent Service Bus trigger definition.</summary>
    /// <returns>A new mutable builder with binding name <c>message</c> and an empty body.</returns>
    public static ServiceBusTriggerBuilder ServiceBusTrigger() => new();

    /// <summary>Starts a fluent Queue Storage trigger definition.</summary>
    /// <returns>A new mutable builder with binding name <c>message</c> and an empty body.</returns>
    public static QueueTriggerBuilder QueueTrigger() => new();

    /// <summary>Starts a fluent Blob Storage trigger definition.</summary>
    /// <returns>A new mutable builder with binding name <c>blob</c> and empty binary content.</returns>
    public static BlobTriggerBuilder BlobTrigger() => new();

    /// <summary>Starts a fluent Event Grid trigger definition.</summary>
    /// <returns>A new mutable builder populated with deterministic test defaults except for generated ID and current event time.</returns>
    public static EventGridTriggerBuilder EventGridTrigger() => new();

    /// <summary>Starts a fluent Event Hubs trigger definition.</summary>
    /// <returns>A new mutable builder with binding name <c>events</c> and an empty batch.</returns>
    public static EventHubsTriggerBuilder EventHubsTrigger() => new();

    /// <summary>Executes a function delegate through configured worker middleware.</summary>
    /// <typeparam name="TFunction">The registered function or service type resolved in a fresh invocation scope.</typeparam>
    /// <param name="context">
    /// The non-null mutable test context used by middleware and the function. It is retained by the
    /// result and remains caller-owned.
    /// </param>
    /// <param name="invoke">
    /// A non-null asynchronous callback invoked at most once with the scoped function instance and
    /// context if middleware reaches the terminal delegate. It must not retain scoped services.
    /// </param>
    /// <returns>
    /// A task whose result reports the context and whether middleware reached the function. The
    /// invocation scope is disposed before completion.
    /// </returns>
    public async Task<TestFunctionInvocationResult> InvokeAsync<TFunction>(
        TestFunctionContext context,
        Func<TFunction, TestFunctionContext, Task> invoke)
        where TFunction : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoke);
        context.FunctionDefinition.ConfigureForFunction(typeof(TFunction));
        return await InvokeInScopeAsync(context, async services =>
        {
            var function = services.GetRequiredService<TFunction>();
            var functionExecuted = false;

            FunctionExecutionDelegate terminal = async workerContext =>
            {
                functionExecuted = true;
                await invoke(function, RequireTestContext(workerContext)).ConfigureAwait(false);
            };

            await BuildPipeline(services, terminal)(context).ConfigureAwait(false);
            return new TestFunctionInvocationResult(context, functionExecuted);
        }).ConfigureAwait(false);
    }

    /// <summary>Executes a value-returning function delegate and captures all returned output properties.</summary>
    /// <typeparam name="TFunction">The registered function or service type resolved in a fresh invocation scope.</typeparam>
    /// <typeparam name="TResult">The function return type whose public output properties are captured.</typeparam>
    /// <param name="context">The non-null mutable context retained by the result and owned by the caller.</param>
    /// <param name="invoke">
    /// A non-null asynchronous callback invoked at most once if middleware reaches the terminal
    /// delegate. It must not retain the scoped function or services.
    /// </param>
    /// <returns>
    /// A task whose result contains the returned value and captured outputs, or the default
    /// <typeparamref name="TResult"/> value when middleware short-circuits. The scope is disposed first.
    /// </returns>
    public async Task<TestFunctionInvocationResult<TResult>> InvokeAsync<TFunction, TResult>(
        TestFunctionContext context,
        Func<TFunction, TestFunctionContext, Task<TResult>> invoke)
        where TFunction : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoke);
        context.FunctionDefinition.ConfigureForFunction(typeof(TFunction));
        return await InvokeInScopeAsync(context, async services =>
        {
            var function = services.GetRequiredService<TFunction>();
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

            await BuildPipeline(services, terminal)(context).ConfigureAwait(false);
            return new TestFunctionInvocationResult<TResult>(context, functionExecuted, result);
        }).ConfigureAwait(false);
    }

    /// <summary>Creates a context, captures trigger input, and invokes a function through middleware.</summary>
    /// <typeparam name="TFunction">The registered function type resolved in a fresh invocation scope.</typeparam>
    /// <typeparam name="TTrigger">The trigger parameter type.</typeparam>
    /// <param name="functionName">The non-empty friendly function name.</param>
    /// <param name="trigger">The non-null trigger value and binding metadata to apply to the new context.</param>
    /// <param name="invoke">
    /// A non-null callback invoked at most once with the scoped function, trigger value, and context.
    /// It must not retain scoped services.
    /// </param>
    /// <param name="cancellationToken">
    /// The token exposed through the new context. The default does not request cancellation.
    /// </param>
    /// <returns>A task whose result reports whether middleware reached the function and exposes the context.</returns>
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
    /// <typeparam name="TFunction">The registered function type resolved in a fresh invocation scope.</typeparam>
    /// <typeparam name="TTrigger">The trigger parameter type.</typeparam>
    /// <typeparam name="TResult">The function return type whose output properties are captured.</typeparam>
    /// <param name="functionName">The non-empty friendly function name.</param>
    /// <param name="trigger">The non-null trigger value and binding metadata to apply to the new context.</param>
    /// <param name="invoke">
    /// A non-null callback invoked at most once with the scoped function, trigger value, and context.
    /// It must not retain scoped services.
    /// </param>
    /// <param name="cancellationToken">
    /// The token exposed through the new context. The default does not request cancellation.
    /// </param>
    /// <returns>
    /// A task whose result contains the function value and captured outputs, or the default result
    /// value when middleware short-circuits.
    /// </returns>
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

    private async Task<TResult> InvokeInScopeAsync<TResult>(
        TestFunctionContext context,
        Func<IServiceProvider, Task<TResult>> invoke)
    {
        await using var scope = _services.CreateAsyncScope();
        context.InstanceServices = scope.ServiceProvider;
        try
        {
            return await invoke(scope.ServiceProvider).ConfigureAwait(false);
        }
        finally
        {
            context.InstanceServices = _services;
        }
    }

    private FunctionExecutionDelegate BuildPipeline(
        IServiceProvider services,
        FunctionExecutionDelegate terminal)
    {
        var next = terminal;
        for (var index = _middleware.Count - 1; index >= 0; index--)
        {
            var middleware = _middleware[index](services);
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
