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
public sealed class RecordedMessageVerificationException(string message) : Exception(message);
