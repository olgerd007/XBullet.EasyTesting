using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TestApi.Models;
using XBullet.EasyTesting;
using XBullet.EasyTesting.Hosting;
using XBullet.EasyTesting.Messaging;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class EventuallyExamples
{
    #region docs-eventually-messages
    [Fact]
    public async Task Waits_for_a_background_publisher()
    {
        using var factory = new TestApiFactory();
        await factory.RunInTestScenarioScopeAsync(async (_, cancellationToken) =>
        {
            // Stand in for work queued to the application's background publisher.
            var publication = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
                factory.PublishedMessages.Record(
                    MessageTransportNames.Kafka, "orders.created", new { OrderId = 42 });
            }, cancellationToken);

            try
            {
                await Eventually.AssertAsync(
                    () => factory.PublishedMessages.Should()
                        .ContainSingle(MessageTransportNames.Kafka, "orders.created")
                        .HavePayload(new { OrderId = 42 }),
                    new EventuallyOptions
                    {
                        Description = "Order 42 is published",
                        ShouldRetry = error => error is RecordedMessageVerificationException
                    },
                    cancellationToken);
            }
            finally
            {
                await publication;
            }
        }, cancellationToken: TestContext.Current.CancellationToken);
    }
    #endregion

    #region docs-eventually-database
    [Fact]
    public async Task Waits_for_a_background_database_write()
    {
        using var factory = new InMemoryTestApiFactory();
        await factory.RunInTestScenarioScopeAsync(async (scope, cancellationToken) =>
        {
            var write = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
                await factory.Database(scope)
                    .Seed(new Product { Id = 42, Name = "Processed", Price = 10m })
                    .ExecuteAsync(cancellationToken);
            }, cancellationToken);

            try
            {
                // Each query opens a fresh DbContext, avoiding stale tracked entities.
                await Eventually.WaitUntilAsync(
                    token => factory.QueryDatabaseAsync(
                        scope,
                        (database, queryToken) => database.Products.AsNoTracking()
                            .AnyAsync(product => product.Id == 42 && product.Name == "Processed", queryToken),
                        token),
                    new EventuallyOptions { Description = "Product 42 is processed" },
                    cancellationToken);
            }
            finally
            {
                await write;
            }
        }, cancellationToken: TestContext.Current.CancellationToken);
    }
    #endregion

    [Fact]
    public async Task Timeout_keeps_scenario_diagnostics_and_the_assertion_failure()
    {
        using var factory = new TestApiFactory();
        var clock = new FakeTimeProvider();
        var exception = await Assert.ThrowsAsync<EventuallyTimeoutException>(() => factory.RunInTestScenarioScopeAsync(
            (_, cancellationToken) => Eventually.AssertAsync(() =>
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                factory.PublishedMessages.Should().HaveCount(1);
            }, new EventuallyOptions { TimeProvider = clock, Timeout = TimeSpan.FromSeconds(1) }, cancellationToken),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.IsType<RecordedMessageVerificationException>(exception.InnerException);
        Assert.IsType<TestScenarioDiagnostics>(exception.Data[TestScenarioDiagnostics.ExceptionDataKey]);
    }
}
