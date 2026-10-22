using Microsoft.DurableTask;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Entities;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Supplies an operation to the SDK's real ITaskEntity dispatcher.</summary>
/// <remarks>Invoke ITaskEntity.RunAsync explicitly. Input is serialized at construction, and null means no input.
/// This helper does not provide worker binding, batching, replay, retries, or transactional rollback.</remarks>
public sealed class TestEntityOperation : TaskEntityOperation
{
    private readonly DataConverter _converter;
    private readonly string? _input;

    /// <summary>Creates an entity operation with shared context and state.</summary>
    /// <param name="context">The non-null entity context.</param>
    /// <param name="name">The non-empty SDK operation name.</param>
    /// <param name="state">The non-null state shared across sequential operations.</param>
    /// <param name="input">The optional input, serialized immediately.</param>
    /// <param name="dataConverter">The input converter, or the SDK JSON default.</param>
    public TestEntityOperation(TestEntityContext context, string name, TestEntityState state,
        object? input = null, DataConverter? dataConverter = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(state);
        Context = context;
        Name = name;
        State = state;
        _converter = dataConverter ?? JsonDataConverter.Default;
        _input = input is null ? null : _converter.Serialize(input);
    }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override TaskEntityContext Context { get; }

    /// <inheritdoc />
    public override TaskEntityState State { get; }

    /// <inheritdoc />
    public override bool HasInput => _input is not null;

    /// <inheritdoc />
    public override object? GetInput(Type inputType)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        return HasInput ? _converter.Deserialize(_input, inputType) : null;
    }
}
