var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.TestAspireApi>("api")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
