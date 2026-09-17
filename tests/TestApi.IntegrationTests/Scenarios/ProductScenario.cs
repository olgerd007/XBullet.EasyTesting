using XBullet.EasyTesting.Hosting;
using TestApi.Models;

namespace TestApi.IntegrationTests.Scenarios;

/// <summary>
/// Example of a test-project-specific scenario that adds product-domain vocabulary
/// without replacing the generic request scenario.
/// </summary>
internal sealed class ProductScenario
{
    private readonly TestApiFactory _factory;
    private readonly TestScenarioScope<Program> _scope;
    private readonly List<Product> _products = [];
    private bool _arranged;

    public ProductScenario(
        TestApiFactory factory,
        TestScenarioScope<Program> scope)
    {
        _factory = factory;
        _scope = scope;
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

    public TestScenarioBuilder<Program> Arrange()
    {
        EnsureNotArranged();
        _arranged = true;
        var products = _products.ToArray();

        return _scope.Scenario()
            .Arrange(token => _factory.Database(_scope)
                .Seed(products)
                .ExecuteAsync(token));
    }

    private void EnsureNotArranged()
    {
        if (_arranged)
        {
            throw new InvalidOperationException(
                "The product scenario has already been arranged.");
        }
    }
}
