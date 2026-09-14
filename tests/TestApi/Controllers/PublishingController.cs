using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TestApi.Messaging;

namespace TestApi.Controllers;

[ApiController]
[Authorize]
[Route("api/publishing")]
public sealed class PublishingController(IApplicationMessagePublisher publisher) : ControllerBase
{
    [HttpPost("kafka/orders")]
    public async Task<IActionResult> PublishOrderToKafka(
        PublishOrderRequest request,
        CancellationToken cancellationToken)
    {
        var message = new OrderCreatedMessage(request.OrderId, request.CustomerId, request.Total);
        await publisher.PublishAsync(
            "Kafka",
            "orders.created",
            message,
            new Dictionary<string, string>
            {
                ["event-type"] = "order-created",
                ["partition-key"] = request.CustomerId
            },
            cancellationToken);

        return Accepted(new PublishReceipt("Kafka", "orders.created"));
    }

    [HttpPost("service-bus/invoices")]
    public async Task<IActionResult> PublishInvoiceToServiceBus(
        PublishInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var message = new InvoiceRequestedMessage(request.InvoiceId, request.AccountId);
        await publisher.PublishAsync(
            "AzureServiceBus",
            "invoice-processing",
            message,
            new Dictionary<string, string>
            {
                ["subject"] = "invoice-requested",
                ["session-id"] = request.AccountId
            },
            cancellationToken);

        return Accepted(new PublishReceipt("AzureServiceBus", "invoice-processing"));
    }

    [HttpPost("notification-hub/push")]
    public async Task<IActionResult> PublishPushNotification(
        PublishNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var message = new PushNotificationMessage(request.Title, request.Body);
        await publisher.PublishAsync(
            "AzureNotificationHubs",
            "customer-notifications",
            message,
            new Dictionary<string, string>
            {
                ["tag"] = $"user:{request.UserId}"
            },
            cancellationToken);

        return Accepted(new PublishReceipt("AzureNotificationHubs", "customer-notifications"));
    }

    public sealed record PublishOrderRequest(
        int OrderId,
        [Required, StringLength(100)] string CustomerId,
        [Range(0.01, 1_000_000)] decimal Total);

    public sealed record PublishInvoiceRequest(
        int InvoiceId,
        [Required, StringLength(100)] string AccountId);

    public sealed record PublishNotificationRequest(
        [Required, StringLength(100)] string UserId,
        [Required, StringLength(100)] string Title,
        [Required, StringLength(500)] string Body);

    public sealed record OrderCreatedMessage(int OrderId, string CustomerId, decimal Total);

    public sealed record InvoiceRequestedMessage(int InvoiceId, string AccountId);

    public sealed record PushNotificationMessage(string Title, string Body);

    public sealed record PublishReceipt(string Transport, string Destination);
}
