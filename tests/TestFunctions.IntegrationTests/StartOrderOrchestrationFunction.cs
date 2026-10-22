using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace TestFunctions.IntegrationTests;

public sealed class StartOrderOrchestrationFunction
{
    [Function(nameof(StartOrderOrchestrationFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData request,
        [DurableClient] DurableTaskClient client,
        CancellationToken cancellationToken)
    {
        var input = await request.ReadAsStringAsync();
        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            "ProcessOrder", input, new StartOrchestrationOptions { InstanceId = "order-42" }, cancellationToken);
        return await client.CreateCheckStatusResponseAsync(request, instanceId, cancellationToken);
    }
}
