# XBullet.EasyTesting.Azure

Deterministic Azure SDK responses, credentials, paging, and HTTP pipeline transport for tests.

The package targets .NET 8, .NET 9, and .NET 10 and depends only on `Azure.Core` plus the core
XBullet package. Service-specific Azure SDK packages remain in the application or test project.

## Install

```shell
dotnet add package XBullet.EasyTesting.Azure
```

## Responses and paging

Create raw responses, model responses, and pageable output without Moq or NSubstitute:

```csharp
var response = AzureTestData.ModelResponse(
    secret,
    status: 200,
    raw => raw.WithHeader("x-ms-request-id", "request-42"));

var page = AzureTestData.Page(
    new[] { firstSecret, secondSecret },
    continuationToken: null);
var pageable = AzureTestData.AsyncPageable(page);
```

Service-specific output models should still be created through the SDK's `*ModelFactory` type.

## Credentials and dependency injection

`TestTokenCredential` returns a deterministic token, captures requested scopes, tenant and claims,
and can arrange authentication failures:

```csharp
var credential = new TestTokenCredential("local-test-token");

using var factory = EasyTestHost.Create<Program>()
    .UseAzureTestCredential(credential)
    .ReplaceAzureClient(secretClient)
    .Build();

factory.RegisterScenarioResource("Azure credential", credential);
```

The credential implements `ITestScenarioResource`, so registering it with a shared factory clears
captured requests between scenarios and includes non-secret request metadata in failure diagnostics.

## Real Azure client pipeline tests

Use `StubAzureHttpPipelineTransport` in any Azure SDK client options to exercise the real client
request serialization, response parsing, and retry pipeline without network access:

```csharp
var transport = new StubAzureHttpPipelineTransport()
    .Respond(status: 500)
    .Respond(status: 200);

var options = new BlobClientOptions
{
    Transport = transport
};
var client = new BlobClient(
    new Uri("https://storage.test/container/blob"),
    new AzureSasCredential("sig=test"),
    options);

var exists = await client.ExistsAsync(cancellationToken);

transport.VerifyCalled(RequestMethod.Head, "/container/blob", expectedCount: 2);
```

Responses are consumed in order, making transient-failure and retry tests deterministic. Recorded
requests include method, URI, headers, and buffered content. Failure diagnostics omit header values,
query strings, and body content so credentials and payloads are not exposed.

## Documentation

- [Documentation home and package selection](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/index.md)
- [Executable Azure SDK examples](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/tests/XBullet.EasyTesting.Tests/AzureTestingTests.cs)
