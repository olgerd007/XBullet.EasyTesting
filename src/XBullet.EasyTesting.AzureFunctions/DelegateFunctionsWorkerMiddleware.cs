using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;

namespace XBullet.EasyTesting.AzureFunctions;

internal sealed class DelegateFunctionsWorkerMiddleware(
    Func<FunctionContext, FunctionExecutionDelegate, Task> invoke)
    : IFunctionsWorkerMiddleware
{
    public Task Invoke(FunctionContext context, FunctionExecutionDelegate next) => invoke(context, next);
}
