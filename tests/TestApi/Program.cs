using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TestApi.Authentication;
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
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IApplicationMessagePublisher, LoggingApplicationMessagePublisher>();
builder.Services
    .AddAuthentication()
    .AddJwtBearer("AzureAd", options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Authentication:Issuer"] ?? "https://login.example.invalid",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Authentication:Audience"] ?? "test-api",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("sample-only-signing-key-at-least-32-bytes")),
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "roles"
        };
    })
    .AddJwtBearer("PartnerBearer", options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://partner.example.invalid",
            ValidateAudience = true,
            ValidAudience = "test-api-partner",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("partner-sample-signing-key-at-least-32-bytes")),
            ValidateLifetime = true
        };
    })
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        "ApiKey",
        options => options.ValidKeys["integration-secret"] = "partner-key")
    .AddCertificate("Certificate", options =>
    {
        options.AllowedCertificateTypes = CertificateTypes.SelfSigned;
        options.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
    });
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
    options.AddPolicy("PartnerOnly", policy =>
    {
        policy.AddAuthenticationSchemes("PartnerBearer");
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("partner", "trusted");
    });
    options.AddPolicy("CertificateOnly", policy =>
    {
        policy.AddAuthenticationSchemes("Certificate");
        policy.RequireAuthenticatedUser();
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
