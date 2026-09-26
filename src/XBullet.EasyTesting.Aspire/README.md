# XBullet.EasyTesting.Aspire

Closed-box distributed application testing helpers for .NET Aspire.

The package targets .NET 8, .NET 9, and .NET 10. Use it when a test needs the complete AppHost topology;
keep using `WebApplicationFactory` and `XBullet.EasyTesting` for fast, single-service in-memory tests.

## Install

```shell
dotnet add package XBullet.EasyTesting.Aspire
```

## Example

Reference the AppHost project from the test project, then start the distributed application and
wait for the resources the test depends on:

```csharp
await AspireTestHost.Create<Projects.Orders_AppHost>()
    .WaitForResource("api")
    .WaitForResource("frontend")
    .RunAsync(async (application, cancellationToken) =>
    {
        using var client = application.CreateHttpClient("frontend");
        using var response = await client.GetAsync("/orders/42", cancellationToken);

        response.EnsureSuccessStatusCode();
    }, cancellationToken);
```

Aspire assigns isolated endpoints and starts the resources declared by the AppHost. The configured
startup timeout covers both AppHost startup and resource health checks. Unavailable resources stop
the wait immediately instead of consuming the complete timeout.

Use `WithArguments(...)` for AppHost command-line arguments and `ConfigureAppHost(...)` when the
native `IDistributedApplicationTestingBuilder` needs test-specific configuration. For manual
lifetime control, call `StartAsync` and dispose the returned application asynchronously.

## Failure diagnostics

`RunAsync` captures an `AspireApplicationDiagnostics` snapshot before shutdown when the test fails
and attaches it to `exception.Data[AspireApplicationDiagnostics.ExceptionDataKey]`. The snapshot
contains each resource's current state, health status, exit code, and bounded recent console logs.
Connection strings and endpoint values are intentionally excluded.

Diagnostics can also be captured explicitly:

```csharp
await using var application = await AspireTestHost.Create<Projects.Orders_AppHost>()
    .WaitForResource("api")
    .StartAsync(cancellationToken);

var diagnostics = await application.CaptureDiagnosticsAsync(cancellationToken);
```

## Documentation

- [Documentation home and package selection](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/index.md)
- [Executable Aspire examples](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/tests/TestApi.IntegrationTests/AspireTests.cs)
