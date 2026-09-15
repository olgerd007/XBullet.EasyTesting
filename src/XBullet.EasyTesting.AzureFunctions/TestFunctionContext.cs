using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>A minimal isolated-worker context with real dependency-injection services.</summary>
public sealed class TestFunctionContext : FunctionContext
{
    private readonly CancellationToken _cancellationToken;
    private TestTraceContext _traceContext;
    private TestRetryContext _retryContext;

    internal TestFunctionContext(
        IServiceProvider instanceServices,
        string functionName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instanceServices);
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        InstanceServices = instanceServices;
        FunctionName = functionName;
        FunctionId = functionName;
        InvocationId = Guid.NewGuid().ToString("N");
        _cancellationToken = cancellationToken;
        _traceContext = new TestTraceContext();
        BindingContext = new TestBindingContext();
        _retryContext = new TestRetryContext();
        FunctionDefinition = new TestFunctionDefinition(functionName);
        Bindings = new TestFunctionBindings();
    }

    /// <summary>Gets the friendly function name assigned by the test.</summary>
    public string FunctionName { get; }

    /// <inheritdoc />
    public override string InvocationId { get; }

    /// <inheritdoc />
    public override string FunctionId { get; }

    /// <inheritdoc />
    public override TraceContext TraceContext => _traceContext;

    /// <summary>Gets the strongly typed test trace context.</summary>
    public TestTraceContext TestTraceContext => _traceContext;

    /// <inheritdoc />
    public override TestBindingContext BindingContext { get; }

    /// <inheritdoc />
    public override RetryContext RetryContext => _retryContext;

    /// <summary>Gets the strongly typed test retry context.</summary>
    public TestRetryContext TestRetryContext => _retryContext;

    /// <inheritdoc />
    public override IServiceProvider InstanceServices { get; set; }

    /// <inheritdoc />
    public override TestFunctionDefinition FunctionDefinition { get; }

    /// <summary>Gets captured input and output bindings for this invocation.</summary>
    public TestFunctionBindings Bindings { get; }

    /// <inheritdoc />
    public override IDictionary<object, object> Items { get; set; } =
        new Dictionary<object, object>();

    /// <inheritdoc />
    public override IInvocationFeatures Features { get; } = new TestInvocationFeatures();

    /// <inheritdoc />
    public override CancellationToken CancellationToken => _cancellationToken;

    /// <summary>Sets retry state and returns this context.</summary>
    public TestFunctionContext WithRetry(int retryCount, int maxRetryCount)
    {
        _retryContext = new TestRetryContext(retryCount, maxRetryCount);
        return this;
    }

    /// <summary>Sets distributed-tracing values and returns this context.</summary>
    public TestFunctionContext WithTrace(string traceParent, string traceState = "")
    {
        ArgumentNullException.ThrowIfNull(traceParent);
        ArgumentNullException.ThrowIfNull(traceState);
        _traceContext = new TestTraceContext(traceParent, traceState);
        return this;
    }

    /// <summary>Adds a value to binding data and returns this context.</summary>
    public TestFunctionContext WithBindingData(string name, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        BindingContext.Set(name, value);
        return this;
    }

    /// <summary>Adds an input value and its function-definition metadata.</summary>
    public TestFunctionContext WithInputBinding(string name, object? value, string bindingType = "input")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingType);
        Bindings.CaptureInput(name, value);
        FunctionDefinition.AddInput(name, bindingType);
        if (value is not null)
        {
            BindingContext.Set(name, value);
        }

        return this;
    }

    /// <summary>Adds an output value and its function-definition metadata.</summary>
    public TestFunctionContext WithOutputBinding(string name, object? value, string bindingType = "output")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingType);
        Bindings.CaptureOutput(name, value, bindingType);
        FunctionDefinition.AddOutput(name, bindingType);
        return this;
    }

    /// <summary>Adds a typed invocation feature and returns this context.</summary>
    public TestFunctionContext WithFeature<T>(T feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        Features.Set(feature);
        return this;
    }

    /// <summary>Adds an invocation item and returns this context.</summary>
    public TestFunctionContext WithItem(object key, object value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        Items[key] = value;
        return this;
    }
}
