using System.Reflection;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Captures input bindings, output bindings, and the invocation return value.</summary>
public sealed class TestFunctionBindings
{
    private readonly Dictionary<string, object?> _inputs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object?> _outputs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _outputTypes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets captured input values by binding name.</summary>
    public IReadOnlyDictionary<string, object?> Inputs => _inputs;

    /// <summary>Gets captured output values by binding name.</summary>
    public IReadOnlyDictionary<string, object?> Outputs => _outputs;

    /// <summary>Gets worker binding types for captured outputs.</summary>
    public IReadOnlyDictionary<string, string> OutputTypes => _outputTypes;

    /// <summary>Gets the unmodified value returned by the function.</summary>
    public object? InvocationResult { get; private set; }

    /// <summary>Starts fluent assertions over captured output bindings.</summary>
    public TestOutputBindingAssertions Should() => new(this);

    /// <summary>Gets a captured input value by binding name.</summary>
    public T? GetInput<T>(string name) => GetValue<T>(_inputs, name, "input");

    /// <summary>Gets a captured output value by binding name.</summary>
    public T? GetOutput<T>(string name) => GetValue<T>(_outputs, name, "output");

    internal void CaptureInput(string name, object? value) => _inputs[name] = value;

    internal void CaptureOutput(string name, object? value, string bindingType = "output")
    {
        _outputs[name] = value;
        _outputTypes[name] = bindingType;
    }

    internal void CaptureInvocationResult(object? result)
    {
        InvocationResult = result;
        if (result is null)
        {
            return;
        }

        var properties = result.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToArray();

        if (properties.Length == 0 || IsScalar(result.GetType()))
        {
            CaptureOutput("$return", result, "return");
            return;
        }

        foreach (var property in properties)
        {
            CaptureOutput(
                property.Name,
                property.GetValue(result),
                GetBindingType(property));
        }
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(Guid) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan);

    private static string GetBindingType(PropertyInfo property)
    {
        var outputAttribute = property.GetCustomAttributes(inherit: true)
            .FirstOrDefault(attribute =>
                attribute.GetType().Name.EndsWith("OutputAttribute", StringComparison.Ordinal));
        if (outputAttribute is null)
        {
            return "output";
        }

        var name = outputAttribute.GetType().Name[..^"Attribute".Length];
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static T? GetValue<T>(
        IReadOnlyDictionary<string, object?> values,
        string name,
        string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!values.TryGetValue(name, out var value))
        {
            throw new KeyNotFoundException($"No {kind} binding named '{name}' was captured.");
        }

        if (value is null)
        {
            return default;
        }

        return value is T typed
            ? typed
            : throw new InvalidCastException(
                $"The {kind} binding '{name}' contains {value.GetType().FullName}, not {typeof(T).FullName}.");
    }
}
