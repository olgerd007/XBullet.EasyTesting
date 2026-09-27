using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Binding metadata used by a test function definition.</summary>
public sealed class TestBindingMetadata : BindingMetadata
{
    /// <summary>Creates binding metadata.</summary>
    /// <param name="name">The non-empty worker binding name.</param>
    /// <param name="type">The non-empty worker binding type, such as <c>httpTrigger</c> or <c>output</c>.</param>
    /// <param name="direction">The input or output direction exposed by the function definition.</param>
    public TestBindingMetadata(string name, string type, BindingDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Name = name;
        Type = type;
        Direction = direction;
    }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override string Type { get; }

    /// <inheritdoc />
    public override BindingDirection Direction { get; }
}
