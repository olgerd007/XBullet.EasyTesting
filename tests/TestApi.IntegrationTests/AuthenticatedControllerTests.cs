using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Authentication;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class AuthenticatedControllerTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public AuthenticatedControllerTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Protected_controller_rejects_anonymous_requests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAnonymousClient();

        var response = await client.GetAsync("/api/secure/me", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_controller_receives_the_test_identity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.Client()
            .AsUser(user => user
                .WithName("Ada")
                .WithNameIdentifier("user-42"))
            .WithoutRedirects()
            .Build();

        var response = await client.GetAsync("/api/secure/me", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Ada", body!.Name);
        Assert.Equal("user-42", body.Subject);
    }

    [Fact]
    public async Task Role_protected_controller_forbids_a_user_without_the_role()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(TestUser.Create());

        var response = await client.GetAsync("/api/secure/admin", cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Role_protected_controller_accepts_a_user_with_the_role()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = TestUser.CreateBuilder()
            .WithRole("Administrator")
            .Build();
        using var client = _factory.Client()
            .AsUser(administrator)
            .Build();

        var response = await client.GetAsync("/api/secure/admin", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Policy_protected_controller_evaluates_custom_claims()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(
            TestUser.Create(claims: [new TestClaim("permission", "reports.read")]));

        var response = await client.GetAsync("/api/secure/reports", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed record CurrentUserResponse(string Name, string Subject);
}
