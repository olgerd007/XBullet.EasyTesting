using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Binding metadata used by a test function definition.</summary>
public sealed class TestBindingMetadata : BindingMetadata
{
    /// <summary>Creates binding metadata.</summary>
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
