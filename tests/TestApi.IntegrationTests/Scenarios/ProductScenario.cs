using TestApi.Models;
using XBullet.EasyTesting.Hosting;

namespace TestApi.IntegrationTests.Scenarios;

/// <summary>
/// Example of a test-project-specific scenario that adds product-domain vocabulary
/// without replacing the generic request scenario.
/// </summary>
#region docs-domain-scenario-base
internal sealed class ProductScenario : Scenario<Program>
{
    private readonly TestApiFactory _factory;
    private readonly List<Product> _products = [];

    public ProductScenario(
        TestApiFactory factory,
        TestScenarioScope<Program> scope)
        : base(scope)
    {
        _factory = factory;
    }

    public const string CollectionUri = "/api/products";

    public ProductScenario WithExistingProduct(
        int id,
        string name,
        decimal price)
    {
        EnsureNotArranged();
        _products.Add(new Product
        {
            Id = id,
            Name = name,
            Price = price
        });
        return this;
    }

    public static string ResourceUri(int id) => $"{CollectionUri}/{id}";

    protected override Task ArrangeCoreAsync(CancellationToken cancellationToken) =>
        _factory.Database(Scope)
            .Seed(_products.ToArray())
            .ExecuteAsync(cancellationToken);
}
#endregion
