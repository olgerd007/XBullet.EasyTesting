using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using TestFunctions.Models;
using TestFunctions.Services;

namespace TestFunctions.Functions;

public sealed class ProcessOrderHttpFunction(ITriggerInvocationSink sink)
{
    [Function(nameof(ProcessOrderHttpFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "orders")] HttpRequestData request,
        FunctionContext context)
    {
        var order = await request.ReadFromJsonAsync<CreateOrderRequest>(context.CancellationToken);
        if (order is null || string.IsNullOrWhiteSpace(order.OrderId) || order.Quantity <= 0)
        {
            var badRequest = request.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(
                new { error = "OrderId and a positive quantity are required." },
                context.CancellationToken);
            return badRequest;
        }

        await sink.RecordAsync(
            new TriggerInvocation("http", order.OrderId, order.Quantity),
            context.CancellationToken);

        var response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(
            new AcceptedOrderResponse(order.OrderId, order.Quantity, "accepted"),
            context.CancellationToken);
        return response;
    }
}
