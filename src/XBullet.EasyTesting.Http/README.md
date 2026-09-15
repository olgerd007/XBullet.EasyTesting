# XBullet.EasyTesting.Http

Fluent outbound HTTP stubs, request matching, fault simulation, and call recording for integration tests.

The package targets .NET 8 and .NET 10.

## Install

```shell
dotnet add package XBullet.EasyTesting.Http
```

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
```

Rules can match queries, headers, text or JSON bodies, and custom predicates. Response sequences, timeouts, cancellation, malformed payloads, and recorded-request assertions are also supported.

See the [repository documentation](https://github.com/olgerd007/XBullet.EasyTesting) for complete examples.
