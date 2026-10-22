using System.Collections.ObjectModel;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Converters;

namespace XBullet.EasyTesting.AzureFunctions;

/// <summary>Builds SDK metadata fixtures with readable typed payloads and deterministic timestamps.</summary>
public sealed class TestOrchestrationMetadataBuilder
{
    private readonly string _name;
    private readonly string _instanceId;
    private OrchestrationRuntimeStatus _status = OrchestrationRuntimeStatus.Pending;
    private DateTimeOffset _createdAt = DateTimeOffset.UnixEpoch;
    private DateTimeOffset _updatedAt = DateTimeOffset.UnixEpoch;
    private DataConverter _converter = JsonDataConverter.Default;
    private object? _input;
    private object? _output;
    private object? _customStatus;
    private TaskFailureDetails? _failure;
    private IReadOnlyDictionary<string, string> _tags = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    /// <summary>Creates a pending-instance fixture.</summary>
    /// <param name="orchestrationName">The non-empty orchestration name.</param>
    /// <param name="instanceId">The non-empty instance ID.</param>
    public TestOrchestrationMetadataBuilder(string orchestrationName, string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestrationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        _name = orchestrationName;
        _instanceId = instanceId;
    }

    /// <summary>Sets a defined SDK runtime status.</summary>
    /// <param name="status">The status to report. Choosing a non-failed status clears failure details.</param>
    /// <returns>This builder for chaining.</returns>
    public TestOrchestrationMetadataBuilder WithRuntimeStatus(OrchestrationRuntimeStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        _status = status;
        if (status != OrchestrationRuntimeStatus.Failed)
        {
            _failure = null;
        }

        return this;
    }

    /// <summary>Sets input serialized when Build is called.</summary>
    /// <typeparam name="T">The input type.</typeparam>
    /// <param name="input">The original input reference; null clears input.</param>
    /// <returns>This builder for chaining.</returns>
    public TestOrchestrationMetadataBuilder WithInput<T>(T input)
    {
        _input = input;
        return this;
    }

    /// <summary>Sets output serialized when Build is called.</summary>
    /// <typeparam name="T">The output type.</typeparam>
    /// <param name="output">The original output reference; null clears output.</param>
    /// <returns>This builder for chaining.</returns>
    public TestOrchestrationMetadataBuilder WithOutput<T>(T output)
    {
        _output = output;
        return this;
    }

    /// <summary>Sets custom status serialized when Build is called.</summary>
    /// <typeparam name="T">The status value type.</typeparam>
    /// <param name="status">The original status reference; null clears custom status.</param>
    /// <returns>This builder for chaining.</returns>
    public TestOrchestrationMetadataBuilder WithCustomStatus<T>(T status)
    {
        _customStatus = status;
        return this;
    }

    /// <summary>Sets the payload converter, also used by the built metadata's typed readers.</summary>
    /// <param name="converter">The non-null caller-owned converter. Defaults to the SDK JSON converter.</param>
    /// <returns>This builder for chaining. The converter is used for all payloads at Build time.</returns>
    public TestOrchestrationMetadataBuilder WithDataConverter(DataConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        _converter = converter;
        return this;
    }

    /// <summary>Sets creation and update times, normalized to UTC.</summary>
    /// <param name="createdAt">The creation time. Defaults to Unix epoch.</param>
    /// <param name="lastUpdatedAt">The update time, which must not precede creation. Defaults to Unix epoch.</param>
    /// <returns>This builder for chaining.</returns>
    public TestOrchestrationMetadataBuilder WithTimestamps(DateTimeOffset createdAt, DateTimeOffset lastUpdatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lastUpdatedAt, createdAt);
        _createdAt = createdAt.ToUniversalTime();
        _updatedAt = lastUpdatedAt.ToUniversalTime();
        return this;
    }

    /// <summary>Copies tags using case-sensitive ordinal keys.</summary>
    /// <param name="tags">The non-null tags to copy. Defaults to empty.</param>
    /// <returns>This builder for chaining.</returns>
    public TestOrchestrationMetadataBuilder WithTags(IReadOnlyDictionary<string, string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        _tags = CopyTags(tags);
        return this;
    }

    /// <summary>Sets failure details and the Failed runtime status.</summary>
    /// <param name="failure">The non-null SDK failure descriptor retained by reference.</param>
    /// <returns>This builder for chaining.</returns>
    public TestOrchestrationMetadataBuilder WithFailure(TaskFailureDetails failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _failure = failure;
        _status = OrchestrationRuntimeStatus.Failed;
        return this;
    }

    /// <summary>Creates SDK failure details from an exception and sets Failed status.</summary>
    /// <param name="exception">The non-null exception whose details may include sensitive text and stack traces.</param>
    /// <returns>This builder for chaining.</returns>
    public TestOrchestrationMetadataBuilder WithFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return WithFailure(TaskFailureDetails.FromException(exception));
    }

    /// <summary>Creates a fresh SDK metadata snapshot, serializing configured payloads.</summary>
    /// <returns>Metadata with typed payload readers enabled, including when payloads are absent.</returns>
    public OrchestrationMetadata Build() => Build(true);

    internal OrchestrationMetadata Build(bool includePayloads) => new(_name, _instanceId)
    {
        RuntimeStatus = _status,
        CreatedAt = _createdAt,
        LastUpdatedAt = _updatedAt,
        DataConverter = includePayloads ? _converter : null,
        SerializedInput = includePayloads ? Serialize(_input) : null,
        SerializedOutput = includePayloads ? Serialize(_output) : null,
        SerializedCustomStatus = includePayloads ? Serialize(_customStatus) : null,
        FailureDetails = includePayloads ? _failure : null,
        Tags = CopyTags(_tags)
    };

    private string? Serialize(object? value) => value is SerializedDurablePayload payload
        ? payload.Data : _converter.Serialize(value);

    internal static OrchestrationMetadata Copy(OrchestrationMetadata metadata, bool includePayloads) => new(metadata.Name, metadata.InstanceId)
    {
        RuntimeStatus = metadata.RuntimeStatus,
        CreatedAt = metadata.CreatedAt,
        LastUpdatedAt = metadata.LastUpdatedAt,
        Tags = CopyTags(metadata.Tags),
        DataConverter = includePayloads ? metadata.DataConverter : null,
        SerializedInput = includePayloads ? metadata.SerializedInput : null,
        SerializedOutput = includePayloads ? metadata.SerializedOutput : null,
        SerializedCustomStatus = includePayloads ? metadata.SerializedCustomStatus : null,
        FailureDetails = includePayloads ? metadata.FailureDetails : null
    };

    private static IReadOnlyDictionary<string, string> CopyTags(IReadOnlyDictionary<string, string> tags) =>
        new ReadOnlyDictionary<string, string>(tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal));
}
