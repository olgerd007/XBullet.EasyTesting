using System.Text.Json;
using System.Text.Json.Nodes;

namespace XBullet.EasyTesting.Messaging;

/// <summary>Fluently verifies messages captured by a recording bus.</summary>
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

        if (_bus.Count != expected)
        {
            throw Failure(
                $"Expected {expected} recorded message(s), but found {_bus.Count}." +
                FormatMessages(_bus.Messages));
        }

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
        var messages = _bus.For(transport, destination);
        if (messages.Count != 1)
        {
            throw Failure(
                $"Expected exactly one message for transport '{transport}' and " +
                $"destination '{destination}', but found {messages.Count}." +
                FormatMessages(_bus.Messages));
        }

        return new RecordedMessageAssertions(messages[0], _bus.SerializerOptions);
    }

    private static string FormatMessages(IReadOnlyList<RecordedMessage> messages) =>
        messages.Count == 0
            ? $"{Environment.NewLine}No messages were recorded."
            : $"{Environment.NewLine}Recorded messages:{Environment.NewLine}" + string.Join(
                Environment.NewLine,
                messages.Select(message => $"- {message.Transport}: {message.Destination}"));

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
