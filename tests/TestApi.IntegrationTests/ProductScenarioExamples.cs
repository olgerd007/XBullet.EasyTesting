using System.Net;
using XBullet.EasyTesting.Hosting;
using TestApi.IntegrationTests.Scenarios;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class ProductScenarioExamples : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ProductScenarioExamples(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public Task Domain_scenario_can_arrange_an_existing_product() =>
        Run(async (scope, cancellationToken) =>
        {
            var products = new ProductScenario(_factory, scope)
                .WithExistingProduct(841, "Desk lamp", 34.95m);

            using var result = await products.Arrange()
                .AsUser(user => user.WithName("Product reader"))
                .Get(ProductScenario.ResourceUri(841))
                .ExecuteAsync(cancellationToken);

            await result.Should()
                .HaveStatusCode(HttpStatusCode.OK)
                .HaveJsonBodyAsync(
                    new ProductResponse(841, "Desk lamp", 34.95m),
                    cancellationToken: cancellationToken);
        });

    [Fact]
    public Task Domain_scenario_can_compose_multiple_arrangements() =>
        Run(async (scope, cancellationToken) =>
        {
            var products = new ProductScenario(_factory, scope)
                .WithExistingProduct(852, "Mouse", 45m)
                .WithExistingProduct(851, "Keyboard", 120m);

            using var result = await products.Arrange()
                .AsUser(user => user.WithName("Catalog reader"))
                .Get(ProductScenario.CollectionUri)
                .ExecuteAsync(cancellationToken);

            await result.Should()
                .HaveStatusCode(HttpStatusCode.OK)
                .HaveJsonBodyAsync(
                    new[]
                    {
                        new ProductResponse(851, "Keyboard", 120m),
                        new ProductResponse(852, "Mouse", 45m)
                    },
                    cancellationToken: cancellationToken);
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
