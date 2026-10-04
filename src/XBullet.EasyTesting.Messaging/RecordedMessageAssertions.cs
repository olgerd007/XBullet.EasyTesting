using System.Text.Json;
using System.Text.Json.Nodes;

namespace XBullet.EasyTesting.Messaging;

/// <summary>Fluently verifies messages captured by a recording bus.</summary>
/// <remarks>
/// Each assertion takes a new point-in-time snapshot. Predicates run outside the recorder lock;
/// their exceptions propagate unchanged. Chained assertions may observe different snapshots.
/// </remarks>
public sealed class RecordedMessageBusAssertions
{
    private readonly RecordedMessageBus _bus;

    internal RecordedMessageBusAssertions(RecordedMessageBus bus)
    {
        _bus = bus;
    }

    /// <summary>Requires exactly the supplied number of recorded messages.</summary>
    /// <param name="expected">The non-negative message count required at assertion time.</param>
    /// <returns>This bus assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">
    /// The current message count does not equal <paramref name="expected"/>.
    /// </exception>
    public RecordedMessageBusAssertions HaveCount(int expected)
    {
        if (expected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expected));
        }

        var messages = _bus.Messages;
        if (messages.Count != expected)
        {
            throw Failure(
                $"Expected {expected} recorded message(s), but found {messages.Count}." +
                FormatMessages(messages));
        }

        return this;
    }

    /// <summary>Requires exactly the supplied number of messages for one route.</summary>
    /// <param name="transport">The non-empty transport identifier matched without regard to case.</param>
    /// <param name="destination">The non-empty destination matched using ordinal, case-sensitive comparison.</param>
    /// <param name="expected">The non-negative count of matching messages required.</param>
    /// <returns>This bus assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">The route count differs from the expected count.</exception>
    public RecordedMessageBusAssertions HaveCount(string transport, string destination, int expected)
    {
        ValidateRoute(transport, destination);
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        var snapshot = _bus.Messages;
        var messages = ForRoute(snapshot, transport, destination);
        if (messages.Count != expected)
        {
            throw Failure(
                $"Expected {expected} message(s) for transport '{transport}' and " +
                $"destination '{destination}', but found {messages.Count}." + FormatMessages(snapshot));
        }

        return this;
    }

    /// <summary>Requires at least one recorded message to satisfy a predicate.</summary>
    /// <param name="predicate">The non-null, read-only predicate evaluated in append order until a match is found.</param>
    /// <returns>This bus assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">No message satisfies the predicate.</exception>
    public RecordedMessageBusAssertions Contain(Func<RecordedMessage, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var snapshot = _bus.Messages;
        if (!snapshot.Any(predicate))
        {
            throw Failure("Expected at least one message matching the predicate, but found 0." + FormatMessages(snapshot));
        }

        return this;
    }

    /// <summary>Requires exactly one recorded message to satisfy a predicate.</summary>
    /// <param name="predicate">The non-null, read-only predicate evaluated once per message in append order.</param>
    /// <returns>A new assertion object bound to the matching message and the bus's serializer options.</returns>
    /// <exception cref="RecordedMessageVerificationException">Zero or multiple messages satisfy the predicate.</exception>
    public RecordedMessageAssertions ContainSingle(Func<RecordedMessage, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var snapshot = _bus.Messages;
        var messages = snapshot.Where(predicate).ToArray();
        if (messages.Length != 1)
        {
            throw Failure(
                $"Expected exactly one message matching the predicate, but found {messages.Length}." + FormatMessages(snapshot));
        }

        return new RecordedMessageAssertions(messages[0], _bus.SerializerOptions);
    }

    /// <summary>Requires no recorded messages for one route.</summary>
    /// <param name="transport">The non-empty transport identifier matched without regard to case.</param>
    /// <param name="destination">The non-empty destination matched using ordinal, case-sensitive comparison.</param>
    /// <returns>This bus assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">At least one message exists for the route.</exception>
    /// <remarks>Only the current snapshot is checked. Wait for known background-work completion before asserting absence.</remarks>
    public RecordedMessageBusAssertions NotContain(string transport, string destination) =>
        HaveCount(transport, destination, 0);

    /// <summary>Requires no recorded message to satisfy a predicate.</summary>
    /// <param name="predicate">The non-null, read-only predicate evaluated in append order until a match is found.</param>
    /// <returns>This bus assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">A message satisfies the predicate.</exception>
    /// <remarks>Only the current snapshot is checked. Wait for known background-work completion before asserting absence.</remarks>
    public RecordedMessageBusAssertions NotContain(Func<RecordedMessage, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var snapshot = _bus.Messages;
        if (snapshot.Any(predicate))
        {
            throw Failure("Expected no message matching the predicate, but found a matching message." + FormatMessages(snapshot));
        }

        return this;
    }

    /// <summary>Requires the entire recorded collection to match an exact ordered sequence of predicates.</summary>
    /// <param name="expectations">
    /// Non-null predicates, one per expected message in append order. The array is copied before
    /// evaluation. An empty sequence requires an empty recorder.
    /// </param>
    /// <returns>This bus assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">The count or a positional predicate does not match.</exception>
    /// <remarks>Order reflects completed recording, not concurrent publish-call start order or broker delivery order.</remarks>
    public RecordedMessageBusAssertions HaveSequence(params Func<RecordedMessage, bool>[] expectations)
    {
        var predicates = CopyExpectations(expectations);
        var snapshot = _bus.Messages;
        VerifySequence(snapshot, predicates, "recorded messages", snapshot);
        return this;
    }

    /// <summary>Requires one route's messages to match an exact ordered sequence of predicates.</summary>
    /// <param name="transport">The non-empty transport identifier matched without regard to case.</param>
    /// <param name="destination">The non-empty destination matched using ordinal, case-sensitive comparison.</param>
    /// <param name="expectations">
    /// Non-null predicates, one per expected route message in append order. The array is copied
    /// before evaluation. An empty sequence requires no messages for the route.
    /// </param>
    /// <returns>This bus assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">The route count or a positional predicate does not match.</exception>
    /// <remarks>Other routes are ignored. Order reflects completed recording, not broker delivery order.</remarks>
    public RecordedMessageBusAssertions HaveSequence(
        string transport,
        string destination,
        params Func<RecordedMessage, bool>[] expectations)
    {
        ValidateRoute(transport, destination);
        var predicates = CopyExpectations(expectations);
        var snapshot = _bus.Messages;
        VerifySequence(
            ForRoute(snapshot, transport, destination),
            predicates,
            $"messages for transport '{transport}' and destination '{destination}'",
            snapshot);
        return this;
    }

    /// <summary>Requires exactly one message for the supplied transport and destination.</summary>
    /// <param name="transport">
    /// The non-empty transport identifier matched without regard to case.
    /// </param>
    /// <param name="destination">
    /// The non-empty destination matched using ordinal, case-sensitive comparison.
    /// </param>
    /// <returns>
    /// A new assertion object bound to the single matching message and the bus's serializer
    /// options.
    /// </returns>
    /// <exception cref="RecordedMessageVerificationException">
    /// The point-in-time message snapshot contains zero or multiple matches.
    /// </exception>
    public RecordedMessageAssertions ContainSingle(
        string transport,
        string destination)
    {
        ValidateRoute(transport, destination);
        var snapshot = _bus.Messages;
        var messages = ForRoute(snapshot, transport, destination);
        if (messages.Count != 1)
        {
            throw Failure(
                $"Expected exactly one message for transport '{transport}' and " +
                $"destination '{destination}', but found {messages.Count}." +
                FormatMessages(snapshot));
        }

        return new RecordedMessageAssertions(messages[0], _bus.SerializerOptions);
    }

    private static string FormatMessages(IReadOnlyList<RecordedMessage> messages) =>
        messages.Count == 0
            ? $"{Environment.NewLine}No messages were recorded."
            : $"{Environment.NewLine}Recorded messages:{Environment.NewLine}" + string.Join(
                Environment.NewLine,
                messages.Select((message, index) =>
                    $"- [{index + 1}] {message.Transport}: {message.Destination} ({message.MessageType}) " +
                    $"Payload: {message.Payload.GetRawText()}"));

    private static void ValidateRoute(string transport, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
    }

    private static IReadOnlyList<RecordedMessage> ForRoute(
        IReadOnlyList<RecordedMessage> snapshot,
        string transport,
        string destination) => snapshot.Where(message =>
            string.Equals(message.Transport, transport, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(message.Destination, destination, StringComparison.Ordinal)).ToArray();

    private static Func<RecordedMessage, bool>[] CopyExpectations(Func<RecordedMessage, bool>[] expectations)
    {
        ArgumentNullException.ThrowIfNull(expectations);
        var copy = expectations.ToArray();
        if (copy.Any(predicate => predicate is null))
        {
            throw new ArgumentException("Sequence predicates must not be null.", nameof(expectations));
        }

        return copy;
    }

    private static void VerifySequence(
        IReadOnlyList<RecordedMessage> messages,
        IReadOnlyList<Func<RecordedMessage, bool>> predicates,
        string description,
        IReadOnlyList<RecordedMessage> snapshot)
    {
        if (messages.Count != predicates.Count)
        {
            throw Failure(
                $"Expected a sequence of {predicates.Count} {description}, but found {messages.Count}." + FormatMessages(snapshot));
        }

        for (var index = 0; index < messages.Count; index++)
        {
            if (!predicates[index](messages[index]))
            {
                throw Failure(
                    $"Expected {description} to match the sequence predicate at position {index + 1}, " +
                    "but the message did not match." + FormatMessages(snapshot));
            }
        }
    }

    private static RecordedMessageVerificationException Failure(string message) => new(message);
}

/// <summary>Fluently verifies one recorded message.</summary>
public sealed class RecordedMessageAssertions
{
    private readonly RecordedMessage _message;
    private readonly JsonSerializerOptions _serializerOptions;

    internal RecordedMessageAssertions(
        RecordedMessage message,
        JsonSerializerOptions serializerOptions)
    {
        _message = message;
        _serializerOptions = serializerOptions;
    }

    /// <summary>Requires the message to contain the supplied header.</summary>
    /// <param name="name">The non-empty header name matched without regard to case.</param>
    /// <returns>This message assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">The header is absent.</exception>
    public RecordedMessageAssertions HaveHeader(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_message.Headers.ContainsKey(name))
        {
            throw Failure($"Expected message header '{name}', but it was not present.");
        }

        return this;
    }

    /// <summary>Requires the message to contain the supplied header value.</summary>
    /// <param name="name">The non-empty header name matched without regard to case.</param>
    /// <param name="expectedValue">
    /// The non-null value compared using ordinal, case-sensitive comparison. An empty value is
    /// accepted. Failed assertions include expected and actual values without redaction.
    /// </param>
    /// <returns>This message assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">
    /// The header is absent or its value differs.
    /// </exception>
    public RecordedMessageAssertions HaveHeader(string name, string expectedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(expectedValue);
        if (!_message.Headers.TryGetValue(name, out var actualValue))
        {
            throw Failure($"Expected message header '{name}', but it was not present.");
        }

        if (!string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
        {
            throw Failure(
                $"Expected message header '{name}' to equal '{expectedValue}', " +
                $"but it was '{actualValue}'.");
        }

        return this;
    }

    /// <summary>Requires the message payload to structurally equal the supplied JSON value.</summary>
    /// <typeparam name="T">The type of expected value serialized for comparison.</typeparam>
    /// <param name="expected">
    /// The value serialized immediately and compared structurally with the recorded JSON payload.
    /// It is not retained or owned and may be <see langword="null"/>.
    /// </param>
    /// <param name="options">
    /// Serialization options for <paramref name="expected"/>, or <see langword="null"/> to use the
    /// recording bus's options. Supplied options are read but not owned, retained, or mutated.
    /// </param>
    /// <returns>This message assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">
    /// The JSON values differ structurally. The exception message includes both payloads without
    /// automatic redaction.
    /// </exception>
    public RecordedMessageAssertions HavePayload<T>(
        T expected,
        JsonSerializerOptions? options = null)
    {
        var expectedJson = JsonSerializer.SerializeToNode(
            expected,
            options ?? _serializerOptions);
        var actualJson = JsonNode.Parse(_message.Payload.GetRawText());
        if (!JsonNode.DeepEquals(expectedJson, actualJson))
        {
            throw Failure(
                $"Expected message payload:{Environment.NewLine}{Format(expectedJson)}" +
                $"{Environment.NewLine}Actual message payload:{Environment.NewLine}{Format(actualJson)}");
        }

        return this;
    }

    /// <summary>Requires the deserialized message payload to satisfy a predicate.</summary>
    /// <typeparam name="T">The target payload type; the recorded compile-time type name is not checked.</typeparam>
    /// <param name="predicate">
    /// The non-null, read-only predicate evaluated once against the deserialized payload, including
    /// <see langword="null"/> for a JSON null or a converter returning null.
    /// </param>
    /// <param name="options">
    /// Deserialization options, or <see langword="null"/> to use the bus's serializer options.
    /// Supplied options are read but not retained, owned, or mutated.
    /// </param>
    /// <returns>This message assertion object so additional checks can be chained.</returns>
    /// <exception cref="RecordedMessageVerificationException">The payload does not satisfy the predicate.</exception>
    /// <remarks>Deserialization and predicate exceptions propagate unchanged. Failure messages include the unredacted payload.</remarks>
    public RecordedMessageAssertions HavePayloadMatching<T>(
        Func<T?, bool> predicate,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var payload = _message.GetPayload<T>(options ?? _serializerOptions);
        if (!predicate(payload))
        {
            throw Failure(
                $"Expected message payload to match the predicate for '{typeof(T).FullName}', but it did not." +
                $"{Environment.NewLine}Actual message payload:{Environment.NewLine}{_message.Payload.GetRawText()}");
        }

        return this;
    }

    private static string Format(JsonNode? value) =>
        value?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";

    private static RecordedMessageVerificationException Failure(string message) => new(message);
}

/// <summary>Thrown when recorded messages do not satisfy a fluent assertion.</summary>
/// <param name="message">
/// The non-null assertion-failure message. Depending on the assertion, it can contain unredacted
/// header values or serialized payloads.
/// </param>
public sealed class RecordedMessageVerificationException(string message) : Exception(message);
