using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestApi.IntegrationTests.Scenarios;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class ScenarioPrimitiveTests : ScopedTest<Program, TestApiFactory>, IClassFixture<TestApiFactory>
{
    public ScenarioPrimitiveTests(TestApiFactory factory)
        : base(factory, TestContext.Current.CancellationToken)
    {
    }

    #region docs-domain-scenario-workflow
    [Fact]
    public Task Arranged_domain_state_supports_multiple_requests_in_one_scope() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var products = new ProductScenario(Factory, scope)
                .WithExistingProduct(861, "Desk lamp", 34.95m);
            await products.ArrangeAsync(cancellationToken);

            using var client = scope.CreateAuthenticatedClient();
            using var first = await client.GetAsync(ProductScenario.ResourceUri(861), cancellationToken);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            using var deleted = await client.DeleteAsync(ProductScenario.ResourceUri(861), cancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

            using var missing = await client.GetAsync(ProductScenario.ResourceUri(861), cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        });
    #endregion

    [Fact]
    public Task Deferred_arrangement_freezes_configuration_and_runs_before_the_request() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var scenario = new ProbeScenario(scope);
            var request = scenario.Arrange();

            Assert.Equal(0, scenario.ArrangementCount);
            Assert.Throws<InvalidOperationException>(scenario.ChangeConfiguration);
            Assert.Throws<InvalidOperationException>(() => scenario.Arrange());
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ArrangeAsync(cancellationToken));

            using var result = await request.Send(async (client, token) =>
            {
                Assert.Equal(1, scenario.ArrangementCount);
                Assert.Equal(token, scenario.ArrangementToken);
                return await client.GetAsync("/health", token);
            }).ExecuteAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
            await Assert.ThrowsAsync<InvalidOperationException>(() => request.ExecuteAsync(cancellationToken));
            Assert.Equal(1, scenario.ArrangementCount);
        });

    [Fact]
    public Task Immediate_arrangement_is_single_use_and_keeps_the_scope_alive() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var scenario = new ProbeScenario(scope);
            await scenario.ArrangeAsync(cancellationToken);

            Assert.Equal(cancellationToken, scenario.ArrangementToken);
            Assert.Throws<InvalidOperationException>(scenario.ChangeConfiguration);
            Assert.Throws<InvalidOperationException>(() => scenario.Arrange());
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ArrangeAsync(cancellationToken));
            Assert.Equal(1, scenario.ArrangementCount);

            using var result = await scope.Scenario().Get("/health").ExecuteAsync(cancellationToken);
            Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Failed_arrangement_cannot_be_retried(bool deferred) =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var failure = new InvalidOperationException("Domain arrangement failed.");
            var scenario = new ProbeScenario(scope, failure);

            var thrown = deferred
                ? await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    scenario.Arrange().Get("/health").ExecuteAsync(cancellationToken))
                : await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ArrangeAsync(cancellationToken));

            Assert.Same(failure, thrown);
            Assert.Equal(1, scenario.ArrangementCount);
            Assert.Throws<InvalidOperationException>(scenario.ChangeConfiguration);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ArrangeAsync(cancellationToken));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Canceled_arrangement_does_not_run_domain_setup_and_cannot_be_retried(bool deferred) =>
        RunAsync(async (scope, cancellationToken) =>
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var scenario = new ProbeScenario(scope);

            if (deferred)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    scenario.Arrange().Get("/health").ExecuteAsync(canceled.Token));
            }
            else
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scenario.ArrangeAsync(canceled.Token));
            }

            Assert.Equal(0, scenario.ArrangementCount);
            await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.ArrangeAsync(cancellationToken));
        });

    [Fact]
    public async Task Scoped_runner_returns_values_after_cleanup_and_creates_fresh_scopes()
    {
        var firstId = await RunAsync((scope, token) =>
        {
            Assert.Equal(TestCancellationToken, token);
            Assert.Equal("configured", scope.Services.GetRequiredService<IConfiguration>()["Scenario:Name"]);
            Factory.PublishedMessages.Record("test", "scoped-test", new { Value = 1 });
            return Task.FromResult(scope.ScenarioId);
        }, configure => configure.ConfigureConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scenario:Name"] = "configured"
            })));

        Assert.Equal(0, Factory.PublishedMessages.Count);
        await RunAsync((scope, _) =>
        {
            Assert.NotEqual(firstId, scope.ScenarioId);
            Assert.Null(scope.Services.GetRequiredService<IConfiguration>()["Scenario:Name"]);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Scoped_runner_preserves_failure_diagnostics_and_releases_the_scope()
    {
        var failure = new InvalidOperationException("Expected test failure.");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync((_, _) =>
            {
                Factory.PublishedMessages.Record("test", "before-failure", new { Value = 1 });
                throw failure;
            }));

        Assert.Same(failure, thrown);
        Assert.IsType<TestScenarioDiagnostics>(thrown.Data[TestScenarioDiagnostics.ExceptionDataKey]);
        Assert.Equal(0, Factory.PublishedMessages.Count);
        await RunAsync((_, _) => Task.CompletedTask);
    }

    [Fact]
    public async Task Result_runner_cleans_up_after_callback_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new TestRunner(Factory, cancellation.Token);
        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.ExecuteAsync<int>((_, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                Factory.PublishedMessages.Record("test", "before-cancellation", new { Value = 1 });
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(0);
            }));

        Assert.Equal(cancellation.Token, thrown.CancellationToken);
        Assert.IsType<TestScenarioDiagnostics>(thrown.Data[TestScenarioDiagnostics.ExceptionDataKey]);
        Assert.Equal(0, Factory.PublishedMessages.Count);
        await RunAsync((_, _) => Task.CompletedTask);
    }

    [Fact]
    public void Base_constructors_reject_missing_scope_or_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new ProbeScenario(null!));
        Assert.Throws<ArgumentNullException>(() => new TestRunner(null!));
    }

    private sealed class ProbeScenario : Scenario<Program>
    {
        private readonly Exception? _failure;

        public ProbeScenario(TestScenarioScope<Program> scope, Exception? failure = null)
            : base(scope)
        {
            _failure = failure;
        }

        public int ArrangementCount { get; private set; }

        public CancellationToken ArrangementToken { get; private set; }

        public void ChangeConfiguration() => EnsureNotArranged();

        protected override Task ArrangeCoreAsync(CancellationToken cancellationToken)
        {
            ArrangementCount++;
            ArrangementToken = cancellationToken;
            return _failure is null ? Task.CompletedTask : Task.FromException(_failure);
        }
    }

    private sealed class TestRunner : ScopedTest<Program, TestApiFactory>
    {
        public TestRunner(TestApiFactory factory, CancellationToken cancellationToken = default)
            : base(factory, cancellationToken)
        {
        }

        public Task<TResult> ExecuteAsync<TResult>(
            Func<TestScenarioScope<Program>, CancellationToken, Task<TResult>> test) => RunAsync(test);
    }
}
