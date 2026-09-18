using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TestStartupApi.Data;
using TestStartupApi.External;
using TestStartupApi.Features;
using TestStartupApi.Messaging;

namespace TestStartupApi;

public sealed class Startup(IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();
        services.AddDbContext<OrdersDbContext>(options => options.UseSqlite(
            configuration.GetConnectionString("Orders") ?? "Data Source=startup-orders.db"));

        services.AddHttpClient<IExternalOrdersClient, ExternalOrdersClient>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["ExternalOrders:BaseUrl"] ?? "https://orders.example.test/");
        });
        services.AddHttpClient<IExternalCustomersClient, ExternalCustomersClient>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["ExternalCustomers:BaseUrl"] ?? "https://customers.example.test/");
        });
        services.AddHttpClient<IPostProviderClient, PostProviderClient>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["PostProvider:BaseUrl"] ?? "https://post.example.test/");
        });
        services.AddSingleton(TimeProvider.System);
        services.Configure<FeatureOptions>(configuration.GetSection(FeatureOptions.SectionName));

        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.AddSingleton<IProducer<string, string>>(_ =>
            new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
                ClientId = configuration["Kafka:ClientId"] ?? "test-startup-api",
                EnableIdempotence = true,
                Acks = Acks.All
            }).Build());
        services.AddSingleton<IKafkaPublisher, ConfluentKafkaPublisher>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["AzureAd:Authority"] ??
                    "https://login.microsoftonline.com/common/v2.0";
                options.Audience = configuration["AzureAd:Audience"] ??
                    "api://replace-with-api-client-id";
                options.MapInboundClaims = false;
            });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy(
                AuthorizationPolicies.OrdersWrite,
                policy => policy.RequireAssertion(context => context.User
                    .FindAll("scp")
                    .SelectMany(claim => claim.Value.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries))
                    .Contains(AuthorizationScopes.OrdersWrite, StringComparer.Ordinal)));
        });
    }

    public void Configure(IApplicationBuilder application)
    {
        using (var scope = application.ApplicationServices.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            database.Database.EnsureCreated();
        }

        application.UseRouting();
        application.UseAuthentication();
        application.UseAuthorization();
        application.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}

public static class AuthorizationPolicies
{
    public const string OrdersWrite = "Orders.Write";
}

public static class AuthorizationScopes
{
    public const string OrdersWrite = "orders.write";
}
