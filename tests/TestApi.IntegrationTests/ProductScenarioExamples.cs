using System.Net;
using TestApi.IntegrationTests.Scenarios;
using XBullet.EasyTesting.Hosting;
using Xunit;

namespace TestApi.IntegrationTests;

#region docs-scoped-test-base
public sealed class ProductScenarioExamples : ScopedTest<Program, TestApiFactory>, IClassFixture<TestApiFactory>
{
    public ProductScenarioExamples(TestApiFactory factory)
        : base(factory, TestContext.Current.CancellationToken)
    {
    }

    [Fact]
    public Task Domain_scenario_can_arrange_an_existing_product() =>
        RunAsync(async (scope, cancellationToken) =>
        {
            var products = new ProductScenario(Factory, scope)
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
        RunAsync(async (scope, cancellationToken) =>
        {
            var products = new ProductScenario(Factory, scope)
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

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
#endregion
