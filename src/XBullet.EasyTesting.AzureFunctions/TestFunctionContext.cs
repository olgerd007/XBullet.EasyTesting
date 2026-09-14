using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>A minimal isolated-worker context with real dependency-injection services.</summary>
public sealed class TestFunctionContext : FunctionContext
{
    private readonly CancellationToken _cancellationToken;

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
    }

    /// <summary>Gets the friendly function name assigned by the test.</summary>
    public string FunctionName { get; }

    /// <inheritdoc />
    public override string InvocationId { get; }

    /// <inheritdoc />
    public override string FunctionId { get; }

    /// <inheritdoc />
    public override TraceContext TraceContext => null!;

    /// <inheritdoc />
    public override BindingContext BindingContext => null!;

    /// <inheritdoc />
    public override RetryContext RetryContext => null!;

    /// <inheritdoc />
    public override IServiceProvider InstanceServices { get; set; }

    /// <inheritdoc />
    public override FunctionDefinition FunctionDefinition => null!;

    /// <inheritdoc />
    public override IDictionary<object, object> Items { get; set; } =
        new Dictionary<object, object>();

    /// <inheritdoc />
    public override IInvocationFeatures Features { get; } = new TestInvocationFeatures();

    /// <inheritdoc />
    public override CancellationToken CancellationToken => _cancellationToken;
}
