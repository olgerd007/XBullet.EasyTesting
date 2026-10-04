using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using XBullet.EasyTesting.Messaging;
using Xunit;

namespace XBullet.EasyTesting.Tests;

public sealed class RecordedMessageCollectionAssertionTests
{
    #region docs-message-collection-assertions
    [Fact]
    public void Collection_assertions_verify_routes_predicates_and_partial_payloads()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record(MessageTransportNames.Kafka, "orders", new OrderMessage(42, OrderState.Created));
        recorder.Record(MessageTransportNames.Kafka, "orders", new OrderMessage(42, OrderState.Shipped));
        recorder.Record(MessageTransportNames.AzureServiceBus, "audit", new OrderMessage(42, OrderState.Created));

        recorder.Should()
            .HaveCount(3)
            .HaveCount(MessageTransportNames.Kafka, "orders", 2)
            .NotContain(MessageTransportNames.Kafka, "dead-letter")
            .NotContain(message => message.GetPayload<OrderMessage>()?.OrderId == 99)
            .Contain(message => message.Destination == "audit");

        recorder.Should()
            .ContainSingle(message => message.Destination == "orders" &&
                message.GetPayload<OrderMessage>()?.State == OrderState.Shipped)
            .HavePayloadMatching<OrderMessage>(payload => payload?.OrderId == 42);
    }
    #endregion

    #region docs-message-sequences
    [Fact]
    public void Sequence_assertions_verify_exact_global_and_route_order()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record(MessageTransportNames.Kafka, "orders", new OrderMessage(42, OrderState.Created));
        recorder.Record(MessageTransportNames.AzureServiceBus, "audit", new OrderMessage(42, OrderState.Created));
        recorder.Record(MessageTransportNames.Kafka, "orders", new OrderMessage(42, OrderState.Shipped));

        recorder.Should().HaveSequence(
            message => message.Destination == "orders" &&
                message.GetPayload<OrderMessage>()?.State == OrderState.Created,
            message => message.Transport == MessageTransportNames.AzureServiceBus && message.Destination == "audit",
            message => message.Destination == "orders" &&
                message.GetPayload<OrderMessage>()?.State == OrderState.Shipped);

        // The audit message is ignored when checking only the Kafka orders route.
        recorder.Should().HaveSequence(MessageTransportNames.Kafka, "orders",
            message => message.GetPayload<OrderMessage>()?.State == OrderState.Created,
            message => message.GetPayload<OrderMessage>()?.State == OrderState.Shipped);
    }
    #endregion

    [Fact]
    public void Route_counts_and_absence_use_transport_and_destination_comparison_rules()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record("Kafka", "orders", new OrderMessage(1, OrderState.Created));
        recorder.Record("kAfKa", "orders", new OrderMessage(2, OrderState.Created));
        recorder.Record("Kafka", "Orders", new OrderMessage(3, OrderState.Created));
        recorder.Record("Other", "orders", new OrderMessage(4, OrderState.Created));
        var assertions = recorder.Should();

        Assert.Same(assertions, assertions.HaveCount("KAFKA", "orders", 2));
        Assert.Same(assertions, assertions.HaveCount("Kafka", "Orders", 1));
        Assert.Same(assertions, assertions.NotContain("Kafka", "missing"));
        var failure = Assert.Throws<RecordedMessageVerificationException>(() => assertions.HaveCount("Kafka", "orders", 1));
        Assert.Contains("expected", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("found 2", failure.Message);
        Assert.Contains("destination 'orders'", failure.Message);
        Assert.Contains("Other", failure.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => assertions.HaveCount("Kafka", "orders", -1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Invalid_routes_are_rejected_by_all_route_assertions(bool invalidTransport)
    {
        var assertions = new RecordedMessageBus().Should();
        var transport = invalidTransport ? " " : "Kafka";
        var destination = invalidTransport ? "orders" : " ";
        var parameter = invalidTransport ? "transport" : "destination";
        Assert.Equal(parameter, Assert.Throws<ArgumentException>(() => assertions.HaveCount(transport, destination, 0)).ParamName);
        Assert.Equal(parameter, Assert.Throws<ArgumentException>(() => assertions.ContainSingle(transport, destination)).ParamName);
        Assert.Equal(parameter, Assert.Throws<ArgumentException>(() => assertions.NotContain(transport, destination)).ParamName);
        Assert.Equal(parameter, Assert.Throws<ArgumentException>(() => assertions.HaveSequence(transport, destination)).ParamName);
        Assert.Throws<ArgumentNullException>(() => assertions.HaveCount(null!, "orders", 0));
        Assert.Throws<ArgumentNullException>(() => assertions.NotContain("Kafka", null!));
    }

    [Fact]
    public void Predicates_select_messages_by_headers_and_reject_ambiguous_matches()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created), new Dictionary<string, string>
        {
            ["correlation-id"] = "first"
        });
        recorder.Record("Kafka", "orders", new OrderMessage(43, OrderState.Created), new Dictionary<string, string>
        {
            ["correlation-id"] = "second"
        });
        var assertions = recorder.Should();
        Assert.Same(assertions, assertions.Contain(message => message.Headers["CORRELATION-ID"] == "second"));
        assertions.ContainSingle(message => message.Headers["correlation-id"] == "second")
            .HaveHeader("correlation-id", "second")
            .HavePayload(new OrderMessage(43, OrderState.Created));
        var failure = Assert.Throws<RecordedMessageVerificationException>(() => assertions.ContainSingle(_ => true));
        Assert.Contains("found 2", failure.Message);
        Assert.Contains("\"orderId\":43", failure.Message);
    }

    [Fact]
    public void Missing_matches_and_unexpected_messages_report_the_snapshot()
    {
        var recorder = new RecordedMessageBus();
        var assertions = recorder.Should();
        Assert.Contains("No messages were recorded", Assert.Throws<RecordedMessageVerificationException>(
            () => assertions.Contain(_ => true)).Message);
        Assert.Contains("found 0", Assert.Throws<RecordedMessageVerificationException>(
            () => assertions.ContainSingle(_ => true)).Message);
        Assert.Same(assertions, assertions.NotContain(_ => true));
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
        Assert.Same(assertions, assertions.NotContain(_ => false));
        Assert.Contains("matching message", Assert.Throws<RecordedMessageVerificationException>(
            () => assertions.NotContain(message => message.Destination == "orders")).Message);
        var failure = Assert.Throws<RecordedMessageVerificationException>(() => assertions.NotContain("kafka", "orders"));
        Assert.Contains("found 1", failure.Message);
        Assert.Contains("\"orderId\":42", failure.Message);
        Assert.Contains("found 0", Assert.Throws<RecordedMessageVerificationException>(() => assertions.Contain(_ => false)).Message);
    }

    [Fact]
    public void Predicates_are_validated_and_callback_failures_propagate_unchanged()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
        var assertions = recorder.Should();
        var failure = new InvalidOperationException("Broken predicate.");
        Func<RecordedMessage, bool> predicate = _ => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => assertions.Contain(predicate)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => assertions.ContainSingle(predicate)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => assertions.NotContain(predicate)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => assertions.HaveSequence(predicate)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => assertions.HaveSequence("Kafka", "orders", predicate)));
        Assert.Throws<ArgumentNullException>(() => assertions.Contain(null!));
        Assert.Throws<ArgumentNullException>(() => assertions.ContainSingle((Func<RecordedMessage, bool>)null!));
        Assert.Throws<ArgumentNullException>(() => assertions.NotContain((Func<RecordedMessage, bool>)null!));
    }

    [Fact]
    public void Predicate_selection_uses_one_snapshot_even_when_the_recorder_is_reset()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
        recorder.Record("Kafka", "orders", new OrderMessage(43, OrderState.Created));
        var visits = new List<int>();
        recorder.Should().ContainSingle(message =>
        {
            recorder.Reset();
            var id = message.GetPayload<OrderMessage>()!.OrderId;
            visits.Add(id);
            return id == 43;
        }).HavePayloadMatching<OrderMessage>(payload => payload?.OrderId == 43);
        Assert.Equal([42, 43], visits);
        recorder.Should().HaveCount(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sequence_counts_reject_extra_missing_and_duplicate_messages(bool routeOnly)
    {
        var recorder = new RecordedMessageBus();
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
        var calls = 0;
        Func<RecordedMessage, bool> predicate = _ => { calls++; return true; };
        var assertions = recorder.Should();
        var failure = Assert.Throws<RecordedMessageVerificationException>(() => Verify([predicate]));
        Assert.Contains("sequence of 1", failure.Message);
        Assert.Contains("found 2", failure.Message);
        Assert.Equal(0, calls);
        failure = Assert.Throws<RecordedMessageVerificationException>(() => Verify([predicate, predicate, predicate]));
        Assert.Contains("sequence of 3", failure.Message);
        Assert.Contains("found 2", failure.Message);
        Assert.Equal(0, calls);
        Assert.Same(assertions, Verify([predicate, predicate]));
        Assert.Equal(2, calls);

        RecordedMessageBusAssertions Verify(Func<RecordedMessage, bool>[] predicates) => routeOnly
            ? assertions.HaveSequence("Kafka", "orders", predicates)
            : assertions.HaveSequence(predicates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sequence_order_failures_identify_the_first_mismatched_position(bool routeOnly)
    {
        var recorder = new RecordedMessageBus();
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Shipped));
        Func<RecordedMessage, bool>[] predicates =
        [
            message => message.GetPayload<OrderMessage>()?.State == OrderState.Created,
            message => message.GetPayload<OrderMessage>()?.State == OrderState.Created
        ];
        var failure = Assert.Throws<RecordedMessageVerificationException>(() => routeOnly
            ? recorder.Should().HaveSequence("Kafka", "orders", predicates)
            : recorder.Should().HaveSequence(predicates));
        Assert.Contains("position 2", failure.Message);
        Assert.Contains("[2] Kafka: orders", failure.Message);
        Assert.Contains("\"state\":1", failure.Message);
        failure = Assert.Throws<RecordedMessageVerificationException>(() => recorder.Should().HaveSequence(
            message => message.GetPayload<OrderMessage>()?.State == OrderState.Shipped,
            message => message.GetPayload<OrderMessage>()?.State == OrderState.Created));
        Assert.Contains("position 1", failure.Message);
    }

    [Fact]
    public void Empty_sequences_and_route_sequences_use_exact_scope_and_case_rules()
    {
        var recorder = new RecordedMessageBus();
        recorder.Should().HaveSequence().HaveSequence("Kafka", "orders");
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
        recorder.Record("Kafka", "Orders", new OrderMessage(43, OrderState.Created));
        recorder.Record("Other", "orders", new OrderMessage(44, OrderState.Created));
        recorder.Should().HaveSequence("kAFka", "orders", message => message.GetPayload<OrderMessage>()?.OrderId == 42);
        recorder.Should().HaveSequence("Kafka", "missing");
        Assert.Throws<RecordedMessageVerificationException>(() => recorder.Should().HaveSequence());
        Assert.Throws<RecordedMessageVerificationException>(() => recorder.Should().HaveSequence("Kafka", "orders"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sequences_copy_expectations_and_messages_before_running_callbacks(bool routeOnly)
    {
        var recorder = new RecordedMessageBus();
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Shipped));
        Func<RecordedMessage, bool>[] predicates = [null!, message => message.GetPayload<OrderMessage>()?.State == OrderState.Shipped];
        predicates[0] = message =>
        {
            recorder.Reset().Record("Kafka", "orders", new OrderMessage(99, OrderState.Created));
            predicates[1] = _ => false;
            return message.GetPayload<OrderMessage>()?.State == OrderState.Created;
        };
        if (routeOnly)
        {
            recorder.Should().HaveSequence("Kafka", "orders", predicates);
        }
        else
        {
            recorder.Should().HaveSequence(predicates);
        }

        recorder.Should().HaveCount(1).ContainSingle("Kafka", "orders").HavePayload(new OrderMessage(99, OrderState.Created));
    }

    [Fact]
    public void Sequence_inputs_are_validated_before_any_predicate_is_run()
    {
        var assertions = new RecordedMessageBus().Should();
        var calls = 0;
        Func<RecordedMessage, bool>[] predicates = [_ => { calls++; return true; }, null!];
        Assert.Throws<ArgumentNullException>(() => assertions.HaveSequence((Func<RecordedMessage, bool>[])null!));
        Assert.Throws<ArgumentNullException>(() => assertions.HaveSequence("Kafka", "orders", null!));
        Assert.Equal("expectations", Assert.Throws<ArgumentException>(() => assertions.HaveSequence(predicates)).ParamName);
        Assert.Equal("expectations", Assert.Throws<ArgumentException>(() => assertions.HaveSequence("Kafka", "orders", predicates)).ParamName);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Payload_predicates_use_bus_options_and_support_explicit_deserialization_options()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        options.Converters.Add(new JsonStringEnumConverter());
        var recorder = new RecordedMessageBus(options);
        recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
        var assertion = recorder.Should().ContainSingle("Kafka", "orders");
        Assert.Same(assertion, assertion.HavePayloadMatching<OrderMessage>(payload => payload?.OrderId == 42 && payload.State == OrderState.Created));

        // Web-default deserialization does not map order_id to OrderId; an explicit naming policy does.
        var webRecorder = new RecordedMessageBus();
        webRecorder.Record("Kafka", "orders", new { order_id = 42 });
        var projectionAssertion = webRecorder.Should().ContainSingle("Kafka", "orders");
        Assert.Throws<RecordedMessageVerificationException>(() =>
            projectionAssertion.HavePayloadMatching<OrderProjection>(payload => payload?.OrderId == 42));
        var projectionOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        Assert.Same(projectionAssertion, projectionAssertion.HavePayloadMatching<OrderProjection>(payload => payload?.OrderId == 42, projectionOptions));
        var failure = Assert.Throws<RecordedMessageVerificationException>(() => assertion.HavePayloadMatching<OrderMessage>(payload => payload?.OrderId == 99));
        Assert.Contains("OrderMessage", failure.Message);
        Assert.Contains("\"order_id\":42", failure.Message);
        Assert.Contains("\"state\":\"Created\"", failure.Message);
    }

    [Fact]
    public void Payload_predicates_handle_null_payloads_and_value_types()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record<string?>("Kafka", "null", null);
        recorder.Record("Kafka", "values", 42);
        recorder.Should().ContainSingle("Kafka", "null").HavePayloadMatching<OrderMessage>(payload => payload is null);
        recorder.Should().ContainSingle("Kafka", "values").HavePayloadMatching<int>(payload => payload == 42);
        Assert.Contains("null", Assert.Throws<RecordedMessageVerificationException>(() => recorder.Should()
            .ContainSingle("Kafka", "null").HavePayloadMatching<OrderMessage>(payload => payload is not null)).Message);
    }

    [Fact]
    public async Task Collection_failure_counts_and_summaries_remain_consistent_during_concurrent_reset()
    {
        var recorder = new RecordedMessageBus();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var started = new ManualResetEventSlim();
        var writer = Task.Factory.StartNew(() =>
        {
            started.Set();
            while (!stop.IsCancellationRequested)
            {
                recorder.Record("Kafka", "orders", new OrderMessage(42, OrderState.Created));
                recorder.Record("Other", "audit", new OrderMessage(42, OrderState.Created));
                recorder.Reset();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            started.Wait(TestContext.Current.CancellationToken);
            for (var index = 0; index < 1_000; index++)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                var global = Assert.Throws<RecordedMessageVerificationException>(() => recorder.Should().HaveCount(int.MaxValue));
                var route = Assert.Throws<RecordedMessageVerificationException>(() => recorder.Should().HaveCount("Kafka", "orders", int.MaxValue));
                Verify(global.Message, line => line.StartsWith("- [", StringComparison.Ordinal));
                Verify(route.Message, line => line.Contains("Kafka: orders", StringComparison.Ordinal));
            }
        }
        finally
        {
            stop.Cancel();
            await writer;
        }

        static void Verify(string diagnostic, Func<string, bool> isMessage)
        {
            var count = int.Parse(Regex.Match(diagnostic, @"but found (\d+)\.").Groups[1].Value);
            Assert.Equal(count, diagnostic.Split(Environment.NewLine).Count(isMessage));
        }
    }

    [Fact]
    public void Payload_predicate_and_deserialization_errors_propagate_unchanged()
    {
        var recorder = new RecordedMessageBus();
        recorder.Record("Kafka", "orders", "not an order");
        var assertion = recorder.Should().ContainSingle("Kafka", "orders");
        var calls = 0;
        Assert.Throws<JsonException>(() => assertion.HavePayloadMatching<OrderMessage>(_ => { calls++; return true; }));
        Assert.Equal(0, calls);
        Assert.Throws<ArgumentNullException>(() => assertion.HavePayloadMatching<OrderMessage>(null!));
        var failure = new InvalidOperationException("Broken payload predicate.");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => assertion.HavePayloadMatching<string>(_ => throw failure)));
    }

    private enum OrderState { Created, Shipped }

    private sealed record OrderMessage(int OrderId, OrderState State);

    private sealed record OrderProjection(int OrderId);
}
