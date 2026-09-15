using System.Collections.Immutable;
using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Configurable metadata for a test function invocation.</summary>
public sealed class TestFunctionDefinition : FunctionDefinition
{
    private ImmutableArray<FunctionParameter> _parameters = [];
    private string _pathToAssembly = string.Empty;
    private string _entryPoint;
    private ImmutableDictionary<string, BindingMetadata> _inputBindings =
        ImmutableDictionary<string, BindingMetadata>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    private ImmutableDictionary<string, BindingMetadata> _outputBindings =
        ImmutableDictionary<string, BindingMetadata>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    internal TestFunctionDefinition(string functionName)
    {
        Name = functionName;
        Id = functionName;
        _entryPoint = functionName;
    }

    /// <inheritdoc />
    public override ImmutableArray<FunctionParameter> Parameters => _parameters;

    /// <inheritdoc />
    public override string PathToAssembly => _pathToAssembly;

    /// <inheritdoc />
    public override string EntryPoint => _entryPoint;

    /// <inheritdoc />
    public override string Id { get; }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override IImmutableDictionary<string, BindingMetadata> InputBindings => _inputBindings;

    /// <inheritdoc />
    public override IImmutableDictionary<string, BindingMetadata> OutputBindings => _outputBindings;

    internal void AddInput(string name, string type) =>
        _inputBindings = _inputBindings.SetItem(
            name,
            new TestBindingMetadata(name, type, BindingDirection.In));

    internal void AddOutput(string name, string type) =>
        _outputBindings = _outputBindings.SetItem(
            name,
            new TestBindingMetadata(name, type, BindingDirection.Out));

    internal void ConfigureForFunction(Type functionType)
    {
        ArgumentNullException.ThrowIfNull(functionType);
        _pathToAssembly = functionType.Assembly.Location;
        var method = functionType.GetMethods()
            .FirstOrDefault(candidate => candidate.GetCustomAttributes(inherit: true)
                .Any(attribute =>
                    attribute.GetType().Name == "FunctionAttribute" &&
                    string.Equals(
                        attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string,
                        Name,
                        StringComparison.Ordinal)));
        if (method is null)
        {
            _entryPoint = functionType.FullName ?? functionType.Name;
            return;
        }

        _entryPoint = $"{functionType.FullName}.{method.Name}";
        _parameters = method.GetParameters()
            .Select(parameter => new FunctionParameter(
                parameter.Name ?? string.Empty,
                parameter.ParameterType))
            .ToImmutableArray();
    }
}
