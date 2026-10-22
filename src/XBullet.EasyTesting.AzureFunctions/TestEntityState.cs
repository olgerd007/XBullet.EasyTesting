using Microsoft.DurableTask;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Entities;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Provides serialized, test-controlled state for SDK entity operations.</summary>
/// <remarks>Share one instance across sequential operations for an entity. Reads deserialize fresh values;
/// changes persist only through SetState. Null deletes state. This is not a transaction or concurrency engine.</remarks>
public sealed class TestEntityState : TaskEntityState
{
    private readonly DataConverter _converter;
    private string? _serializedState;

    /// <summary>Creates state with an optional initial value and converter.</summary>
    /// <param name="initialState">A value to serialize immediately; null means absent state.</param>
    /// <param name="dataConverter">The converter, or the SDK JSON default.</param>
    public TestEntityState(object? initialState = null, DataConverter? dataConverter = null)
    {
        _converter = dataConverter ?? JsonDataConverter.Default;
        SetState(initialState);
    }

    /// <inheritdoc />
    public override bool HasState => _serializedState is not null;

    /// <inheritdoc />
    public override object? GetState(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return HasState ? _converter.Deserialize(_serializedState, type) : null;
    }

    /// <inheritdoc />
    public override void SetState(object? state) =>
        _serializedState = state is null ? null : _converter.Serialize(state);
}
