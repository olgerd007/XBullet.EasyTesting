using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Hosting;
using Microsoft.EntityFrameworkCore;
using TestApi.Models;
using Xunit;

namespace TestApi.IntegrationTests;

#region docs-minimal-controller-test
public sealed class MinimalControllerDocumentationExample : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public MinimalControllerDocumentationExample(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authenticated_user_can_call_a_protected_controller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        using var client = scope.Client()
            .AsUser(user => user
                .WithName("Ada")
                .WithNameIdentifier("user-42"))
            .WithoutRedirects()
            .Build();

        using var response = await client.GetAsync(
            "/api/secure/me",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
#endregion

#region docs-realistic-controller-scenario
public sealed class RealisticControllerDocumentationExample : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public RealisticControllerDocumentationExample(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Scenario_arranges_data_and_verifies_the_response()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);

        using var result = await scope.Scenario()
            .Arrange(token => _factory.Database(scope)
                .Seed(new Product
                {
                    Id = 841,
                    Name = "Desk lamp",
                    Price = 34.95m
                })
                .ExecuteAsync(token))
            .AsUser(user => user.WithName("Product reader"))
            .Get("/api/products/841")
            .ExecuteAsync(cancellationToken);

        await result.Should()
            .HaveStatusCode(HttpStatusCode.OK)
            .HaveJsonBodyAsync(
                new ProductResponse(841, "Desk lamp", 34.95m),
                cancellationToken: cancellationToken);
    }

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
#endregion

#region docs-controller-failure-diagnostics
public sealed class FailureDiagnosticsDocumentationExample : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public FailureDiagnosticsDocumentationExample(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Failed_assertion_describes_expected_and_actual_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        using var result = await scope.Scenario()
            .AsAnonymous()
            .Get("/api/secure/me")
            .ExecuteAsync(cancellationToken);

        var exception = Assert.Throws<TestHttpResponseVerificationException>(
            () => result.Should().HaveStatusCode(HttpStatusCode.OK));

        Assert.Contains("200 (OK)", exception.Message);
        Assert.Contains("401 (Unauthorized)", exception.Message);
    }
}
#endregion

#region docs-authenticated-crud
public sealed class AuthenticatedCrudDocumentationExample : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public AuthenticatedCrudDocumentationExample(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authenticated_client_can_create_read_update_and_delete_a_product()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTestScenarioScopeAsync(
            cancellationToken: cancellationToken);
        using var client = scope.Client()
            .AsUser(user => user.WithName("CRUD tester"))
            .Build();

        using var createResponse = await client.PostAsJsonAsync(
            "/api/products",
            new ProductRequest("Webcam", 79.95m),
            cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponse>(
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);

        var resourceUri = $"/api/products/{created.Id}";
        using var readResponse = await client.GetAsync(resourceUri, cancellationToken);
        var read = await readResponse.Content.ReadFromJsonAsync<ProductResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(created, read);

        using var updateResponse = await client.PutAsJsonAsync(
            resourceUri,
            new ProductRequest("Conference webcam", 99.95m),
            cancellationToken);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProductResponse>(
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Conference webcam", updated!.Name);

        using var deleteResponse = await client.DeleteAsync(resourceUri, cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var remainingProducts = await _factory.QueryDatabaseAsync(
            scope,
            (database, token) => database.Products.CountAsync(token),
            cancellationToken);
        Assert.Equal(0, remainingProducts);
    }

    private sealed record ProductRequest(string Name, decimal Price);

    private sealed record ProductResponse(int Id, string Name, decimal Price);
}
#endregion
