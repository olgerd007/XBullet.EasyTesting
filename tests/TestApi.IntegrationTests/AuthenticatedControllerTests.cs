using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Hosting;
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
    public Task Protected_controller_rejects_anonymous_requests() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.CreateAnonymousClient();

            using var response = await client.GetAsync("/api/secure/me", cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });

    [Fact]
    public Task Protected_controller_receives_the_test_identity() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.Client()
                .AsUser(user => user
                    .WithName("Ada")
                    .WithNameIdentifier("user-42"))
                .WithoutRedirects()
                .Build();

            using var response = await client.GetAsync("/api/secure/me", cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Ada", body!.Name);
            Assert.Equal("user-42", body.Subject);
        });

    [Fact]
    public Task Role_protected_controller_forbids_a_user_without_the_role() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.CreateAuthenticatedClient(TestUser.Create());

            using var response = await client.GetAsync("/api/secure/admin", cancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        });

    [Fact]
    public Task Role_protected_controller_accepts_a_user_with_the_role() =>
        Run(async (scope, cancellationToken) =>
        {
            var administrator = TestUser.CreateBuilder()
                .WithRole("Administrator")
                .Build();
            using var client = scope.Client()
                .AsUser(administrator)
                .Build();

            using var response = await client.GetAsync("/api/secure/admin", cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        });

    [Fact]
    public Task Policy_protected_controller_evaluates_custom_claims() =>
        Run(async (scope, cancellationToken) =>
        {
            using var client = scope.CreateAuthenticatedClient(
                TestUser.Create(claims: [new TestClaim("permission", "reports.read")]));

            using var response = await client.GetAsync("/api/secure/reports", cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        });

    private Task Run(Func<TestScenarioScope<Program>, CancellationToken, Task> test) =>
        _factory.RunInTestScenarioScopeAsync(
            test,
            cancellationToken: TestContext.Current.CancellationToken);

    private sealed record CurrentUserResponse(string Name, string Subject);
}
