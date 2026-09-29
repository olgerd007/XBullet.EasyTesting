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

## Bounded capture

Long-running or payload-heavy tests can bound retained data without changing the default behavior:

```csharp
var stub = new StubHttpMessageHandler(new StubHttpMessageHandlerOptions
{
    MaximumRecordedExchanges = 500,
    MaximumRequestBodyBytes = 64 * 1024,
    MaximumResponseBodyBytes = 64 * 1024
});
```

`CallCount` continues to count every received request, while `Requests` and `Exchanges` expose the
newest retained entries. A zero exchange limit disables retention. Truncated request and response
records set `BodyTruncated`; body capture can also be disabled independently. Request matchers and
response factories observe the configured captured request body, so do not truncate or disable it
when a rule depends on content beyond the retained portion.

## Documentation

- [API reference](https://olgerd007.github.io/XBullet.EasyTesting/api/packages/xbullet-easytesting-http.html)
- [Outbound HTTP guide](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/outbound-http.md)
- [Executable HTTP-stub examples](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/tests/XBullet.EasyTesting.Tests/StubHttpMessageHandlerTests.cs)
- [Snapshot adapters for recorded HTTP exchanges](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/src/XBullet.EasyTesting.Snapshots.Http/README.md)
