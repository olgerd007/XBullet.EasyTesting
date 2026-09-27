# Test an Aspire distributed application

Use `XBullet.EasyTesting.Aspire` when a test must start the complete AppHost topology, discover
resources and endpoints, wait for health, and exercise services across process boundaries. For a
single ASP.NET Core service, prefer the faster in-memory host described in
[test-host selection](../concepts/test-hosts.md).

## Prerequisites and installation

Reference the Aspire AppHost project from the test project and install:

```shell
dotnet add package XBullet.EasyTesting.Aspire
```

Import `XBullet.EasyTesting.Aspire`. The AppHost and every resource it launches must be runnable on
the test machine; container resources also require their normal container runtime.

## Start, wait, and call a resource

`RunAsync` owns startup and shutdown. Declare every resource whose health is required before the
test callback runs:

```csharp
await AspireTestHost.Create<Projects.Orders_AppHost>()
    .WithArguments("--environment=Testing")
    .WaitForResource("api")
    .WaitForResource("frontend")
    .WithStartupTimeout(TimeSpan.FromMinutes(2))
    .RunAsync(async (application, cancellationToken) =>
    {
        using var client = application.CreateHttpClient("frontend");
        using var response = await client.GetAsync("/orders/42", cancellationToken);
        response.EnsureSuccessStatusCode();
    }, cancellationToken);
```

`WithArguments` configures AppHost command-line arguments. `ConfigureAppHost` exposes the native
`IDistributedApplicationTestingBuilder` for test-only service or resource configuration. Builder
configuration is frozen after startup begins.

The startup timeout covers AppHost startup and configured health waits. Caller cancellation remains
an `OperationCanceledException`; it is not rewritten as a timeout.

## Discover resources and endpoints

After startup, use:

- `ResourceNames` to inspect discovered logical resources.
- `WaitForResourceAsync` for an additional health wait.
- `GetEndpoint(resourceName, endpointName?)` for an assigned URI.
- `CreateHttpClient(resourceName, endpointName?)` for an endpoint-aware client.
- `GetConnectionStringAsync` for resources that publish a connection string.

Use manual lifetime control when setup or assertions must occur outside one callback:

<!-- snippet: tests/TestApi.IntegrationTests/AspireTests.cs#docs-aspire-manual-lifecycle -->
```csharp
[Fact(Explicit = true)]
public async Task Distributed_app_exposes_all_resource_operations_and_disposes_idempotently()
{
    var builder = AspireTestHost.Create<testapphost::TestAppHost.AppHostMarker>()
        .ConfigureAppHost(_ => { })
        .WaitForResource("api")
        .WithMaximumDiagnosticLinesPerResource(1);
    var application = await builder.StartAsync(TestContext.Current.CancellationToken);
    Assert.Throws<InvalidOperationException>(() => builder.WithArguments("--late"));
    Assert.Throws<InvalidOperationException>(() => builder.ConfigureAppHost(_ => { }));
    Assert.Throws<InvalidOperationException>(() => builder.WaitForResource("api"));
    Assert.Throws<InvalidOperationException>(() =>
        builder.WithStartupTimeout(TimeSpan.FromSeconds(1)));
    Assert.Throws<InvalidOperationException>(() =>
        builder.WithMaximumDiagnosticLinesPerResource(1));

    Assert.Contains("api", application.ResourceNames);
    Assert.NotNull(application.GetEndpoint("api"));
    Assert.NotNull(application.GetEndpoint("api", "http"));
    Assert.Throws<ArgumentException>(() => application.GetEndpoint("api", " "));
    await application.WaitForResourceAsync("api", TestContext.Current.CancellationToken);
    await Assert.ThrowsAsync<ArgumentException>(async () =>
        await application.GetConnectionStringAsync(
            "api",
            TestContext.Current.CancellationToken));
    using (var client = application.CreateHttpClient("api"))
    using (var response = await client.GetAsync("/health", TestContext.Current.CancellationToken))
    {
        response.EnsureSuccessStatusCode();
    }

    using (var client = application.CreateHttpClient("api", "http"))
    using (var response = await client.GetAsync("/health", TestContext.Current.CancellationToken))
    {
        response.EnsureSuccessStatusCode();
    }

    var diagnostics = await application.CaptureDiagnosticsAsync(
        TestContext.Current.CancellationToken);
    Assert.All(diagnostics.Resources, resource => Assert.True(resource.Logs.Count <= 1));

    await application.DisposeAsync();
    await application.DisposeAsync();
    Assert.Throws<ObjectDisposedException>(() => application.GetEndpoint("api"));
}
```
<!-- end-snippet -->

Always dispose the returned application asynchronously. Disposal is idempotent and shuts down the
distributed application even after a test failure.

## Failure diagnostics and logs

When the callback supplied to `RunAsync` throws, the host captures diagnostics before shutdown and
attaches them to `exception.Data[AspireApplicationDiagnostics.ExceptionDataKey]`:

<!-- snippet: tests/TestApi.IntegrationTests/AspireTests.cs#docs-aspire-run-diagnostics -->
```csharp
[Fact(Explicit = true)]
public async Task Distributed_app_waits_for_health_serves_http_and_attaches_failure_diagnostics()
{
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        AspireTestHost.Create<testapphost::TestAppHost.AppHostMarker>()
            .WithArguments("--environment=Testing")
            .ConfigureAppHost(_ => { })
            .WaitForResource("api")
            .WithStartupTimeout(TimeSpan.FromMinutes(2))
            .RunAsync(
                async (application, cancellationToken) =>
                {
                    Assert.Contains("api", application.ResourceNames);
                    Assert.NotNull(application.GetEndpoint("api"));
                    using var client = application.CreateHttpClient("api");
                    using var response = await client.GetAsync("/health", cancellationToken);
                    response.EnsureSuccessStatusCode();
                    Assert.Contains(
                        "Healthy",
                        await response.Content.ReadAsStringAsync(cancellationToken));
                    throw new InvalidOperationException("Expected distributed test failure.");
                },
                TestContext.Current.CancellationToken));

    var diagnostics = Assert.IsType<AspireApplicationDiagnostics>(
        exception.Data[AspireApplicationDiagnostics.ExceptionDataKey]);
    var api = Assert.Single(diagnostics.Resources, resource => resource.Name == "api");
    Assert.Equal("Healthy", api.HealthStatus);
    Assert.NotEmpty(api.Logs);
}
```
<!-- end-snippet -->

Each resource snapshot contains its logical and instance names, state, health, exit code, and a
bounded tail of stdout and stderr with stream identity. Endpoint values and connection strings are
excluded. Configure the bound with `WithMaximumDiagnosticLinesPerResource`; use zero only when log
capture is intentionally disabled.

Call `CaptureDiagnosticsAsync` explicitly when a test needs to inspect current state without
failing. Captured diagnostics are a snapshot and remain available after shutdown.

## Troubleshooting startup and readiness

A startup deadline produces `TimeoutException` and cleans partially created state:

<!-- snippet: tests/TestApi.IntegrationTests/AspireTests.cs#docs-aspire-timeout -->
```csharp
[Fact(Explicit = true)]
public async Task Startup_timeout_is_reported_as_timeout_and_cleans_partial_state()
{
    await Assert.ThrowsAsync<TimeoutException>(() =>
        AspireTestHost.Create<testapphost::TestAppHost.AppHostMarker>()
            .WithStartupTimeout(TimeSpan.FromMilliseconds(1))
            .StartAsync(TestContext.Current.CancellationToken));
}
```
<!-- end-snippet -->

When startup or readiness fails:

1. Verify the AppHost project reference and the marker type passed to `Create<TAppHost>()`.
2. Confirm the resource name matches the AppHost logical name exactly.
3. Inspect attached resource state, health, exit code, and recent logs.
4. Check prerequisites for child processes and containers.
5. Increase the startup timeout only after determining that the resource is progressing normally.

An unavailable resource fails its health wait rather than consuming the entire timeout. Missing
endpoint or connection-string errors usually mean the selected resource does not publish that kind
of value or the endpoint name is wrong.

These tests exercise the packaged topology, service discovery, and real inter-process networking.
They are slower and more environment-dependent than in-memory controller tests, so keep focused
unit and service-level tests as the default feedback loop. Canonical behavior lives in
[`AspireTests`](../../tests/TestApi.IntegrationTests/AspireTests.cs); real distributed tests are
explicit and currently run for the AppHost target framework.

Browse the [Aspire API reference](../api/packages/xbullet-easytesting-aspire.md).
