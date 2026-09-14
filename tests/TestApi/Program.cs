using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TestApi.Data;
using TestApi.External;
using TestApi.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<TestApiDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("TestApi") ?? "Data Source=test-api.db"));
builder.Services.AddHttpClient<IExternalCatalogClient, ExternalCatalogClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["ExternalCatalog:BaseUrl"] ?? "https://catalog.example.test/"));
builder.Services.AddSingleton<IApplicationMessagePublisher, LoggingApplicationMessagePublisher>();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("CanReadReports", policy => policy.RequireClaim("permission", "reports.read"));
    options.AddPolicy("AzureAdOnly", policy =>
    {
        policy.AddAuthenticationSchemes("AzureAd");
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("tid", "tenant-42");
        policy.RequireClaim("scp", "orders.read");
    });
    options.AddPolicy("ApiKeyOnly", policy =>
    {
        policy.AddAuthenticationSchemes("ApiKey");
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("api_key_id", "partner-key");
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<TestApiDbContext>();
    await database.Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
