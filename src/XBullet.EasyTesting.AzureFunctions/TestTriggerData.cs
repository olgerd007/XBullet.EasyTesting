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
    public T Value { get; }

    /// <summary>Gets the trigger binding name.</summary>
    public string BindingName { get; }

    /// <summary>Gets the worker binding type.</summary>
    public string BindingType { get; }

    /// <summary>Gets trigger metadata exposed through <see cref="Microsoft.Azure.Functions.Worker.BindingContext"/>.</summary>
    public IReadOnlyDictionary<string, object?> BindingData => _bindingData;

    /// <summary>Captures this input and its metadata in a function context.</summary>
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
