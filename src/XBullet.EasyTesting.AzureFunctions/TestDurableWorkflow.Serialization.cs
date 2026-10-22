using Microsoft.DurableTask;
using Microsoft.DurableTask.Converters;

namespace XBullet.EasyTesting.AzureFunctions;

/// <inheritdoc />
public sealed partial class TestDurableWorkflow
{
    private DataConverter? _dataConverter;

    /// <summary>Enables serialization at connected workflow payload boundaries.</summary>
    /// <param name="dataConverter">The caller-owned, concurrently usable SDK converter; null selects the SDK JSON default.</param>
    /// <returns>This workflow for chaining. Configure before scheduling or after a successful reset.</returns>
    /// <remarks>Snapshots root/child inputs, activity inputs/results, orchestration results, events, and custom status.
    /// Reads deserialize to the requested type. Recordings retain original references. Reset preserves configuration.
    /// This checks converter compatibility without emulating worker bindings, history, or replay.</remarks>
    public TestDurableWorkflow WithSerialization(DataConverter? dataConverter = null)
    {
        lock (_gate)
        {
            EnsureRegistrationAvailable();
            _dataConverter = dataConverter ?? JsonDataConverter.Default;
        }

        return this;
    }

    private object? CapturePayload(object? value) => SerializedDurablePayload.Capture(value, _dataConverter);
}
