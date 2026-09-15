using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Mutable binding data exposed through an isolated-worker function context.</summary>
public sealed class TestBindingContext : BindingContext
{
    private readonly Dictionary<string, object?> _bindingData =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> BindingData => _bindingData;

    internal void Set(string name, object? value) => _bindingData[name] = value;
}
