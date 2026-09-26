# XBullet.EasyTesting.Http

Fluent outbound HTTP stubs, request matching, fault simulation, and call recording for integration tests.

The package targets .NET 8, .NET 9, and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.Http
```

## Example

```csharp
var stub = new StubHttpMessageHandler();

stub.When(HttpMethod.Get, "/products/42")
    .WithRequestHeader("X-Tenant", "tenant-7")
    .RespondJson(new { Id = 42, Name = "Keyboard" });

var client = new HttpClient(stub)
{
    BaseAddress = new Uri("https://catalog.test")
};

using var response = await client.GetAsync("/products/42");
stub.VerifyCalled(HttpMethod.Get, "/products/42");

var exchange = Assert.Single(stub.Exchanges);
Assert.Equal(200, exchange.Response?.StatusCode);
```

Rules can match queries, headers, text or JSON bodies, and custom predicates. Response sequences,
timeouts, cancellation, malformed payloads, and recorded-request assertions are also supported.
`Exchanges` records each request together with its response or failure; response bytes and content
read failures are observed without eagerly consuming the body.

## Documentation

- [External APIs and application-boundary guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/application-boundaries.md)
- [Executable HTTP-stub examples](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/tests/XBullet.EasyTesting.Tests/StubHttpMessageHandlerTests.cs)
- [Snapshot adapters for recorded HTTP exchanges](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/src/XBullet.EasyTesting.Snapshots.Http/README.md)
