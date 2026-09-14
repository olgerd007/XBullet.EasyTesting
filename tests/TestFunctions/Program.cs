using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TestFunctions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services => services.AddSingleton<ITriggerInvocationSink, LoggingTriggerInvocationSink>())
    .Build();

host.Run();
