namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>A trigger value together with binding metadata applied to a test context.</summary>
/// <typeparam name="T">The function parameter type.</typeparam>
public sealed class TestTriggerData<T>
{
    private readonly IReadOnlyDictionary<string, object?> _bindingData;

    internal TestTriggerData(
        T value,
        string bindingName,
        string bindingType,
        IReadOnlyDictionary<string, object?>? bindingData = null)
    {
        Value = value;
        BindingName = bindingName;
        BindingType = bindingType;
        _bindingData = bindingData ?? new Dictionary<string, object?>();
    }

    /// <summary>Gets the value passed to the function trigger parameter.</summary>
    /// <value>The trigger value retained by this instance without cloning.</value>
    public T Value { get; }

    /// <summary>Gets the trigger binding name.</summary>
    /// <value>The non-empty input binding name.</value>
    public string BindingName { get; }

    /// <summary>Gets the worker binding type.</summary>
    /// <value>The non-empty worker binding type added to the function definition.</value>
    public string BindingType { get; }

    /// <summary>Gets trigger metadata exposed through <see cref="Microsoft.Azure.Functions.Worker.BindingContext"/>.</summary>
    /// <value>
    /// The retained read-only metadata dictionary. Values are not cloned or redacted and may contain
    /// sensitive data.
    /// </value>
    public IReadOnlyDictionary<string, object?> BindingData => _bindingData;

    /// <summary>Captures this input and its metadata in a function context.</summary>
    /// <param name="context">The non-null mutable context to update in place.</param>
    /// <returns>The same context, for chaining, after input metadata and binding data are applied.</returns>
    public TestFunctionContext ApplyTo(TestFunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.WithInputBinding(BindingName, Value, BindingType);
        foreach (var pair in _bindingData)
        {
            context.BindingContext.Set(pair.Key, pair.Value);
        }

        return context;
    }
}
