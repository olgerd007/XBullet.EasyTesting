using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TestFunctions.External;
using TestFunctions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<ITriggerInvocationSink, LoggingTriggerInvocationSink>();
        services.AddHttpClient<IOrderPricingClient, OrderPricingClient>(client =>
            client.BaseAddress = new Uri(
                context.Configuration["OrderPricing:BaseUrl"]
                ?? "https://pricing.example.test/"));
    })
    .Build();

host.Run();
