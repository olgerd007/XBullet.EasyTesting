using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class ObservabilityTests
{
    private static readonly DateTimeOffset StartTime =
        new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task Captures_structured_logs_scopes_and_fake_time_from_the_scenario_host()
    {
        using var factory = TestApiHostSettings.CreateBuilder()
            .UseObservability(options => options.UseFakeTime(StartTime))
            .Build();
        await using var scope = await factory.CreateTestScenarioScopeAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var observability = scope.GetObservability();
        var logger = scope.Services.GetRequiredService<ILogger<ObservabilityTests>>();

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = "scenario-42"
        }))
        {
            logger.LogInformation(
                new EventId(42, "OrderProcessed"),
                "Processed order {OrderId}",
                701);
        }

        var entry = Assert.Single(
            observability.Logs.Entries,
            entry => entry.Category == typeof(ObservabilityTests).FullName);
        Assert.Equal(StartTime, entry.Timestamp);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(42, entry.EventId.Id);
        Assert.Equal("Processed order {OrderId}", entry.MessageTemplate);
        Assert.Equal(701, entry.State["OrderId"]);
        var capturedScope = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            Assert.Single(entry.Scopes));
        Assert.Equal("scenario-42", capturedScope["CorrelationId"]);
        observability.Logs.Should()
            .ContainMessage("Processed order 701", LogLevel.Information);

        var delay = Task.Delay(
            TimeSpan.FromMinutes(5),
            scope.Services.GetRequiredService<TimeProvider>(),
            TestContext.Current.CancellationToken);
        Assert.False(delay.IsCompleted);
        observability.Time!.Advance(TimeSpan.FromMinutes(5));
        await delay;
        Assert.Equal(StartTime.AddMinutes(5), observability.Time.GetUtcNow());
    }

    [Fact]
    public async Task Captured_logs_are_included_in_failure_diagnostics()
    {
        using var factory = TestApiHostSettings.CreateBuilder()
            .UseObservability()
            .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            factory.RunInTestScenarioScopeAsync(
                (scope, _) =>
                {
                    using var source = new ActivitySource("Diagnostics.ActivitySource");
                    using var activity = source.StartActivity("diagnostic-operation");
                    activity?.SetTag("failure.kind", "expected");
                    activity?.Stop();

                    using var meter = new Meter("Diagnostics.Meter");
                    meter.CreateCounter<long>("diagnostic.failures").Add(1);

                    scope.Services
                        .GetRequiredService<ILogger<ObservabilityTests>>()
                        .LogError("Diagnostic failure {Code}", 503);
                    throw new InvalidOperationException("Expected test failure.");
                },
                cancellationToken: TestContext.Current.CancellationToken));

        var diagnostics = Assert.IsType<TestScenarioDiagnostics>(
            exception.Data[TestScenarioDiagnostics.ExceptionDataKey]);
        var serialized = JsonSerializer.Serialize(diagnostics);
        Assert.Contains("Observability", serialized);
        Assert.Contains("Diagnostic failure 503", serialized);
        Assert.Contains("Code", serialized);
        Assert.Contains("diagnostic-operation", serialized);
        Assert.Contains("diagnostic.failures", serialized);
    }

    [Fact]
    public async Task Captures_activity_relationships_events_status_and_metrics()
    {
        const string sourceName = "EasyTesting.Tests.Orders";
        const string meterName = "EasyTesting.Tests.OrderMetrics";
        using var factory = TestApiHostSettings.CreateBuilder()
            .UseObservability(options => options
                .UseFakeTime(StartTime)
                .CaptureActivitySource(sourceName)
                .CaptureMeter(meterName))
            .Build();
        await using var scope = await factory.CreateTestScenarioScopeAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var observability = scope.GetObservability();

        using (var source = new ActivitySource(sourceName))
        {
            using var parent = source.StartActivity("orders.process", ActivityKind.Consumer);
            Assert.NotNull(parent);
            parent.SetTag("messaging.destination", "orders");
            parent.AddBaggage("tenant.id", "tenant-42");

            using var child = source.StartActivity("orders.persist", ActivityKind.Internal);
            Assert.NotNull(child);
            child.AddEvent(new ActivityEvent(
                "database.saved",
                tags: new ActivityTagsCollection
                {
                    ["entity.count"] = 1
                }));
            child.SetStatus(ActivityStatusCode.Ok);
            child.Stop();
            parent.SetStatus(ActivityStatusCode.Error, "Expected sample status");
            parent.Stop();
        }

        var parentEntry = Assert.Single(
            observability.Activities.Entries,
            entry => entry.OperationName == "orders.process");
        var childEntry = Assert.Single(
            observability.Activities.Entries,
            entry => entry.OperationName == "orders.persist");
        Assert.Equal(parentEntry.TraceId, childEntry.TraceId);
        Assert.Equal(parentEntry.SpanId, childEntry.ParentSpanId);
        Assert.Equal(ActivityStatusCode.Error, parentEntry.Status);
        Assert.Equal("orders", parentEntry.Tags["messaging.destination"]);
        Assert.Equal("tenant-42", parentEntry.Baggage["tenant.id"]);
        var activityEvent = Assert.Single(childEntry.Events);
        Assert.Equal("database.saved", activityEvent.Name);
        Assert.Equal(1, activityEvent.Tags["entity.count"]);
        observability.Activities.Should()
            .ContainActivity("orders.process", ActivityStatusCode.Error, sourceName)
            .ContainActivity("orders.persist", ActivityStatusCode.Ok, sourceName);

        using var meter = new Meter(meterName);
        var counter = meter.CreateCounter<long>("orders.processed", unit: "orders");
        var duration = meter.CreateHistogram<double>("orders.duration", unit: "ms");
        var queueDepth = 7;
        _ = meter.CreateObservableGauge("orders.queue.depth", () => queueDepth);
        counter.Add(3, new KeyValuePair<string, object?>("region", "eu"));
        duration.Record(12.5);
        observability.Metrics.CollectObservableMeasurements();

        observability.Metrics.Should()
            .ContainMeasurement("orders.processed", 3L, meterName)
            .ContainMeasurement("orders.duration", 12.5, meterName)
            .ContainMeasurement("orders.queue.depth", 7, meterName);
        var counterMeasurement = Assert.Single(
            observability.Metrics.Measurements,
            measurement => measurement.InstrumentName == "orders.processed");
        Assert.Equal("orders", counterMeasurement.Unit);
        Assert.Equal("eu", counterMeasurement.Tags["region"]);
        Assert.Equal(StartTime, counterMeasurement.Timestamp);
    }

    [Fact]
    public void Log_capture_is_bounded_to_the_configured_entry_count()
    {
        using var collector = new TestLogCollector(maximumEntries: 2);
        var logger = collector.CreateLogger("Bounded");

        logger.LogInformation("First");
        logger.LogInformation("Second");
        logger.LogInformation("Third");

        Assert.Equal(2, collector.Count);
        collector.Should()
            .NotContainMessage("First")
            .ContainMessage("Second")
            .ContainMessage("Third");
    }

    [Fact]
    public void Fluent_assertions_cover_success_failure_filters_and_empty_diagnostics()
    {
        using var logs = new TestLogCollector();
        logs.Should().HaveCount(0).NotContainMessage("missing");
        Assert.Throws<ArgumentOutOfRangeException>(() => logs.Should().HaveCount(-1));
        Assert.Throws<TestLogVerificationException>(() => logs.Should().HaveCount(1));
        Assert.Throws<ArgumentException>(() => logs.Should().ContainMessage(" "));
        Assert.Throws<TestLogVerificationException>(() => logs.Should().ContainMessage("missing"));
        logs.CreateLogger("Coverage").LogWarning("Covered message {Value}", 42);
        logs.Should().ContainMessage("Covered message 42", LogLevel.Warning, "Coverage");
        Assert.Throws<TestLogVerificationException>(() =>
            logs.Should().ContainMessage("Covered", LogLevel.Error, "Coverage"));
        Assert.Throws<TestLogVerificationException>(() =>
            logs.Should().NotContainMessage("Covered", LogLevel.Warning, "Coverage"));
        logs.Should().NotContainMessage("Covered", category: "Other");

        const string sourceName = "Coverage.Activity";
        using var activities = new TestActivityCollector([sourceName]);
        activities.Should().HaveCount(0).NotContainActivity("missing");
        Assert.Throws<ArgumentOutOfRangeException>(() => activities.Should().HaveCount(-1));
        Assert.Throws<TestActivityVerificationException>(() => activities.Should().HaveCount(1));
        Assert.Throws<ArgumentException>(() => activities.Should().ContainActivity(" "));
        Assert.Throws<TestActivityVerificationException>(() =>
            activities.Should().ContainActivity("missing"));
        using (var source = new ActivitySource(sourceName))
        using (var activity = source.StartActivity("covered"))
        {
            activity!.SetStatus(ActivityStatusCode.Ok);
        }

        activities.Should().ContainActivity("covered", ActivityStatusCode.Ok, sourceName);
        Assert.Throws<TestActivityVerificationException>(() =>
            activities.Should().ContainActivity("covered", ActivityStatusCode.Error, sourceName));
        Assert.Throws<TestActivityVerificationException>(() =>
            activities.Should().NotContainActivity("covered", sourceName));
        activities.Should().NotContainActivity("covered", "Other");

        const string meterName = "Coverage.Meter";
        using var metrics = new TestMetricCollector(meterNames: [meterName]);
        metrics.Should().HaveCount(0).NotContainMeasurement("missing");
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.Should().HaveCount(-1));
        Assert.Throws<TestMetricVerificationException>(() => metrics.Should().HaveCount(1));
        Assert.Throws<ArgumentException>(() => metrics.Should().ContainMeasurement(" ", 1));
        Assert.Throws<TestMetricVerificationException>(() =>
            metrics.Should().ContainMeasurement("missing", 1));
        using (var meter = new Meter(meterName))
        {
            meter.CreateCounter<int>("covered").Add(7);
        }

        metrics.Should().ContainMeasurement("covered", 7, meterName);
        Assert.Throws<TestMetricVerificationException>(() =>
            metrics.Should().ContainMeasurement("covered", 8, meterName));
        Assert.Throws<TestMetricVerificationException>(() =>
            metrics.Should().NotContainMeasurement("covered", meterName));
        metrics.Should().NotContainMeasurement("covered", "Other");
    }
}
