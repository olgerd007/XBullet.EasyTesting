using System.Text.Json;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Messaging;

/// <summary>Thread-safe, in-memory recording bus used by test publisher adapters.</summary>
public sealed class RecordedMessageBus : ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly List<RecordedMessage> _messages = [];
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>Creates a recorder with optional JSON serialization settings.</summary>
    public RecordedMessageBus(JsonSerializerOptions? serializerOptions = null)
    {
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <summary>Gets a stable copy of all messages in publication order.</summary>
    public IReadOnlyList<RecordedMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }

    /// <summary>Gets the number of messages recorded since construction or the last reset.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count;
            }
        }
    }

    /// <summary>Records a serialized copy of one published message.</summary>
    public void Record<T>(
        string transport,
        string destination,
        T payload,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var serializedPayload = JsonSerializer.SerializeToElement(payload, _serializerOptions);
        var capturedHeaders = headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        var message = new RecordedMessage(
            transport,
            destination,
            typeof(T).FullName ?? typeof(T).Name,
            serializedPayload,
            capturedHeaders);

        lock (_gate)
        {
            _messages.Add(message);
        }
    }

    /// <summary>Records a serialized copy of one published message asynchronously.</summary>
    public Task RecordAsync<T>(
        string transport,
        string destination,
        T payload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Record(transport, destination, payload, headers);
        return Task.CompletedTask;
    }

    /// <summary>Returns messages for one transport and destination.</summary>
    public IReadOnlyList<RecordedMessage> For(
        string transport,
        string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        lock (_gate)
        {
            return _messages
                .Where(message =>
                    string.Equals(message.Transport, transport, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(message.Destination, destination, StringComparison.Ordinal))
                .ToArray();
        }
    }

    /// <summary>Removes every recorded message and returns this recorder.</summary>
    public RecordedMessageBus Reset()
    {
        lock (_gate)
        {
            _messages.Clear();
        }

        return this;
    }

    /// <inheritdoc />
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reset();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<object?>(new
        {
            Count,
            Messages
        });
    }
}
