using System.Net;
using System.Net.Http.Json;
using XBullet.EasyTesting.Authentication;
using XBullet.EasyTesting.Messaging;
using Xunit;

namespace TestApi.IntegrationTests;

public sealed class PublishingControllerTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public PublishingControllerTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Controller_publishes_order_to_kafka_topic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.PublishedMessages.Reset();
        using var client = CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/publishing/kafka/orders",
            new { OrderId = 42, CustomerId = "customer-7", Total = 125.50m },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var recorded = Assert.Single(
            _factory.PublishedMessages.For(MessageTransportNames.Kafka, "orders.created"));
        Assert.Equal("order-created", recorded.Headers["event-type"]);
        Assert.Equal("customer-7", recorded.Headers["partition-key"]);
        Assert.Equal(
            new OrderCreatedMessage(42, "customer-7", 125.50m),
            recorded.GetPayload<OrderCreatedMessage>());
    }

    [Fact]
    public async Task Controller_publishes_invoice_to_service_bus_queue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.PublishedMessages.Reset();
        using var client = CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/publishing/service-bus/invoices",
            new { InvoiceId = 84, AccountId = "account-3" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var recorded = Assert.Single(
            _factory.PublishedMessages.For(
                MessageTransportNames.AzureServiceBus,
                "invoice-processing"));
        Assert.Equal("invoice-requested", recorded.Headers["subject"]);
        Assert.Equal("account-3", recorded.Headers["session-id"]);
        Assert.Equal(
            new InvoiceRequestedMessage(84, "account-3"),
            recorded.GetPayload<InvoiceRequestedMessage>());
    }

    [Fact]
    public async Task Controller_publishes_push_to_notification_hub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.PublishedMessages.Reset();
        using var client = CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/publishing/notification-hub/push",
            new { UserId = "user-9", Title = "Ready", Body = "Your order is ready." },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var recorded = Assert.Single(
            _factory.PublishedMessages.For(
                MessageTransportNames.AzureNotificationHubs,
                "customer-notifications"));
        Assert.Equal("user:user-9", recorded.Headers["tag"]);
        Assert.Equal(
            new PushNotificationMessage("Ready", "Your order is ready."),
            recorded.GetPayload<PushNotificationMessage>());
    }

    [Fact]
    public async Task Anonymous_request_does_not_publish_a_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.PublishedMessages.Reset();
        using var client = _factory.CreateAnonymousClient();

        using var response = await client.PostAsJsonAsync(
            "/api/publishing/kafka/orders",
            new { OrderId = 42, CustomerId = "customer-7", Total = 125.50m },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _factory.PublishedMessages.Count);
    }

    [Fact]
    public async Task Invalid_request_does_not_publish_a_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.PublishedMessages.Reset();
        using var client = CreateAuthenticatedClient();

        using var response = await client.PostAsJsonAsync(
            "/api/publishing/kafka/orders",
            new { OrderId = 42, CustomerId = string.Empty, Total = 0m },
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.PublishedMessages.Count);
    }

    private HttpClient CreateAuthenticatedClient() =>
        _factory.Client()
            .AsUser(TestUser.Create(name: "Publisher"))
            .Build();

    private sealed record OrderCreatedMessage(int OrderId, string CustomerId, decimal Total);

    private sealed record InvoiceRequestedMessage(int InvoiceId, string AccountId);

    private sealed record PushNotificationMessage(string Title, string Body);
}
