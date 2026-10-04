using System.Text.Json;
using XBullet.EasyTesting.Hosting;

namespace XBullet.EasyTesting.Messaging;

/// <summary>Thread-safe, in-memory recording bus used by test publisher adapters.</summary>
/// <remarks>
/// Payload serialization occurs before the message is appended, so concurrent messages are ordered
/// by completed recording rather than call start. Recorded headers, payloads, and diagnostics are
/// not automatically redacted; use only test-safe values.
/// </remarks>
public sealed class RecordedMessageBus : ITestScenarioResource
{
    private readonly object _gate = new();
    private readonly List<RecordedMessage> _messages = [];
    private readonly Dictionary<MessageRoute, List<RecordedMessage>> _messagesByRoute =
        new(MessageRouteComparer.Instance);
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>Creates a recorder with optional JSON serialization settings.</summary>
    /// <param name="serializerOptions">
    /// Options used to serialize every recorded payload and, by default, expected assertion
    /// payloads. When <see langword="null"/>, a new web-default instance is created. A non-null
    /// instance is retained but not owned or disposed; do not mutate it after recording begins.
    /// </param>
    public RecordedMessageBus(JsonSerializerOptions? serializerOptions = null)
    {
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <summary>Gets a stable copy of all messages in publication order.</summary>
    /// <value>
    /// A newly allocated array containing the recorded message objects in append order. Later
    /// records and resets do not change the array. The bus owns each message and its header
    /// dictionary; callers must not mutate them.
    /// </value>
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
    /// <value>The current message count as a thread-safe point-in-time value.</value>
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

    /// <summary>Starts a fluent assertion chain over the recorded messages.</summary>
    /// <returns>
    /// A new assertion object referencing this bus. Each bus-level assertion reads current recorder
    /// state when it runs.
    /// </returns>
    public RecordedMessageBusAssertions Should() => new(this);

    /// <summary>Records a serialized copy of one published message.</summary>
    /// <typeparam name="T">
    /// The compile-time payload type recorded in <see cref="RecordedMessage.MessageType"/> and used
    /// for JSON serialization.
    /// </typeparam>
    /// <param name="transport">
    /// The non-empty transport identifier. Custom values and names from
    /// <see cref="MessageTransportNames"/> are accepted.
    /// </param>
    /// <param name="destination">
    /// The non-empty transport-specific queue, topic, hub, or other destination name.
    /// </param>
    /// <param name="payload">
    /// The payload serialized immediately to a self-contained <see cref="JsonElement"/>. It may be
    /// <see langword="null"/> and is not retained, owned, or disposed.
    /// </param>
    /// <param name="headers">
    /// Headers copied immediately into a case-insensitive dictionary, or <see langword="null"/> to
    /// record no headers. The bus does not retain, own, or mutate the supplied dictionary. Values
    /// are stored without redaction.
    /// </param>
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
            var route = new MessageRoute(transport, destination);
            if (!_messagesByRoute.TryGetValue(route, out var routeMessages))
            {
                routeMessages = [];
                _messagesByRoute.Add(route, routeMessages);
            }

            routeMessages.Add(message);
        }
    }

    /// <summary>Records a serialized copy of one published message asynchronously.</summary>
    /// <typeparam name="T">
    /// The compile-time payload type recorded in <see cref="RecordedMessage.MessageType"/> and used
    /// for JSON serialization.
    /// </typeparam>
    /// <param name="transport">
    /// The non-empty transport identifier. Custom values and names from
    /// <see cref="MessageTransportNames"/> are accepted.
    /// </param>
    /// <param name="destination">
    /// The non-empty transport-specific queue, topic, hub, or other destination name.
    /// </param>
    /// <param name="payload">
    /// The payload serialized synchronously to a self-contained <see cref="JsonElement"/>. It may
    /// be <see langword="null"/> and is not retained, owned, or disposed.
    /// </param>
    /// <param name="headers">
    /// Headers copied synchronously into a case-insensitive dictionary, or
    /// <see langword="null"/> to record no headers. The bus does not retain, own, or mutate the
    /// supplied dictionary. Values are stored without redaction.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels before serialization or recorder mutation. After the initial check, recording
    /// completes synchronously and no longer observes cancellation. The default token does not
    /// request cancellation.
    /// </param>
    /// <returns>A task that is already complete after the message has been recorded.</returns>
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
    /// <param name="transport">
    /// The non-empty transport identifier matched without regard to case.
    /// </param>
    /// <param name="destination">
    /// The non-empty destination matched using ordinal, case-sensitive comparison.
    /// </param>
    /// <returns>
    /// A newly allocated array containing matching handler-owned message objects in append order.
    /// Later records and resets do not change the array.
    /// </returns>
    public IReadOnlyList<RecordedMessage> For(
        string transport,
        string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        lock (_gate)
        {
            return _messagesByRoute.TryGetValue(
                new MessageRoute(transport, destination),
                out var messages)
                ? messages.ToArray()
                : [];
        }
    }

    /// <summary>Removes every recorded message and returns this recorder.</summary>
    /// <returns>
    /// This recorder for reuse. Messages appended concurrently after the reset lock is released are
    /// retained.
    /// </returns>
    public RecordedMessageBus Reset()
    {
        lock (_gate)
        {
            _messages.Clear();
            _messagesByRoute.Clear();
        }

        return this;
    }

    /// <summary>Removes every recorded message.</summary>
    /// <param name="cancellationToken">
    /// Cancels before any state is changed. Once cancellation is checked, reset completes
    /// synchronously. The default token does not request cancellation.
    /// </param>
    /// <returns>A value task that is already complete after the messages have been cleared.</returns>
    public ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reset();
        return ValueTask.CompletedTask;
    }

    /// <summary>Captures the current recorded-message state for scenario diagnostics.</summary>
    /// <param name="cancellationToken">
    /// Cancels before diagnostic state is copied. The default token does not request cancellation.
    /// </param>
    /// <returns>
    /// An already-completed value task containing a newly allocated serializable object with the
    /// count and message-array copy from the same point-in-time snapshot. Later records and resets
    /// do not change this snapshot. Headers and JSON payloads are included without automatic redaction.
    /// </returns>
    public ValueTask<object?> CaptureDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var messages = Messages;
        return ValueTask.FromResult<object?>(new
        {
            Count = messages.Count,
            Messages = messages
        });
    }

    internal JsonSerializerOptions SerializerOptions => _serializerOptions;

    private readonly record struct MessageRoute(string Transport, string Destination);

    private sealed class MessageRouteComparer : IEqualityComparer<MessageRoute>
    {
        public static MessageRouteComparer Instance { get; } = new();

        public bool Equals(MessageRoute x, MessageRoute y) =>
            string.Equals(x.Transport, y.Transport, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Destination, y.Destination, StringComparison.Ordinal);

        public int GetHashCode(MessageRoute route) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(route.Transport),
            StringComparer.Ordinal.GetHashCode(route.Destination));
    }
}
