# Test Azure SDK clients

Use `XBullet.EasyTesting.Azure` to build Azure SDK responses and pages, provide a deterministic
credential, replace clients in DI, or exercise a real Azure client through an offline HTTP pipeline.
It depends on `Azure.Core`; service-specific SDK packages stay in the application or test project.

## Install

```shell
dotnet add package XBullet.EasyTesting.Azure
```

Import `XBullet.EasyTesting.Azure`, `Azure`, and `Azure.Core`, plus the namespace of the
service-specific SDK client under test.

Choose the narrowest boundary that proves the behavior:

- Return `AzureTestData` from an application-owned client abstraction for fast business tests.
- Replace an Azure client and credential in DI to test application composition.
- Use `StubAzureHttpPipelineTransport` to test the real SDK client's serialization, parsing, and
  retry pipeline without network access.

## Raw and model responses

`TestAzureResponse` supplies status, headers, reason phrase, and binary or JSON content. Convert it
to `Response<T>` with `FromValue`, or use `AzureTestData.ModelResponse`:

<!-- snippet: tests/XBullet.EasyTesting.Tests/AzureTestingTests.cs#docs-azure-responses -->
```csharp
[Fact]
public void Response_exposes_content_headers_and_model_value()
{
    using var raw = new TestAzureResponse(201)
        .WithHeader("x-ms-request-id", "request-42")
        .WithJsonContent(new { name = "Ada" });
    var response = raw.FromValue("created");

    Assert.Equal("created", response.Value);
    Assert.Equal(201, response.GetRawResponse().Status);
    Assert.Equal("request-42", response.GetRawResponse().Headers.RequestId);
    Assert.Equal("application/json", response.GetRawResponse().Headers.ContentType);
    Assert.Contains("Ada", response.GetRawResponse().Content.ToString());
}
```
<!-- end-snippet -->

Create service output models with the service SDK's `*ModelFactory`; these helpers wrap the model in
the Azure response contract rather than bypassing its public construction model.

## Pageable results

Build pages with explicit continuation tokens, then combine them into synchronous or asynchronous
pageable results:

<!-- snippet: tests/XBullet.EasyTesting.Tests/AzureTestingTests.cs#docs-azure-paging -->
```csharp
[Fact]
public async Task Pageable_helpers_preserve_pages_and_continuation_tokens()
{
    var first = AzureTestData.Page([1, 2], "next");
    var last = AzureTestData.Page([3]);
    var pageable = AzureTestData.AsyncPageable(first, last);
    var pages = new List<Page<int>>();

    await foreach (var page in pageable.AsPages()
        .WithCancellation(TestContext.Current.CancellationToken))
    {
        pages.Add(page);
    }

    Assert.Equal(2, pages.Count);
    Assert.Equal("next", pages[0].ContinuationToken);
    Assert.Equal([1, 2, 3], pages.SelectMany(page => page.Values));
}
```
<!-- end-snippet -->

Preserve page boundaries when code under test reacts to continuation tokens or performs one action
per page. A flattened list is enough when only enumeration behavior matters.

## Credentials and dependency injection

`TestTokenCredential` returns a deterministic token, records scopes, claims, tenant, and parent
request ID, and can arrange a failure:

<!-- snippet: tests/XBullet.EasyTesting.Tests/AzureTestingTests.cs#docs-azure-credential -->
```csharp
[Fact]
public async Task Credential_returns_deterministic_token_records_context_and_can_fail()
{
    var expiresOn = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    var credential = new TestTokenCredential("token-42", expiresOn);
    var context = new TokenRequestContext(
        ["https://storage.azure.com/.default"],
        parentRequestId: "parent-1",
        claims: "claims-1",
        tenantId: "tenant-1");

    var token = await credential.GetTokenAsync(
        context,
        TestContext.Current.CancellationToken);

    Assert.Equal("token-42", token.Token);
    Assert.Equal(expiresOn, token.ExpiresOn);
    var request = Assert.Single(credential.Requests);
    Assert.Equal(context.Scopes, request.Scopes);
    Assert.Equal("tenant-1", request.TenantId);

    credential.FailWith(new InvalidOperationException("Expected test failure."));
    await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await credential.GetTokenAsync(
            context,
            TestContext.Current.CancellationToken));
    Assert.Equal(2, credential.RequestCount);
}
```
<!-- end-snippet -->

Replace Azure dependencies through the host builder:

```csharp
var credential = new TestTokenCredential("local-test-token");
var secretClient = CreateTestSecretClient();

using var factory = EasyTestHost.Create<Program>()
    .UseAzureTestCredential(credential)
    .ReplaceAzureClient(secretClient)
    .Build();

factory.RegisterScenarioResource("Azure credential", credential);
```

Scenario registration resets captured requests and contributes non-secret credential metadata to
failure diagnostics. A replacement verifies application behavior, not Azure identity validation.

## Exercise a real SDK pipeline

Inject `StubAzureHttpPipelineTransport` into the service client's options. Ordered responses make a
retry policy deterministic while the real client still creates the request and parses the result:

<!-- snippet: tests/XBullet.EasyTesting.Tests/AzureTestingTests.cs#docs-azure-pipeline -->
```csharp
[Fact]
public async Task Transport_exercises_real_blob_client_and_records_retries()
{
    var transport = new StubAzureHttpPipelineTransport()
        .Respond(status: 500)
        .Respond(status: 200);
    var options = new BlobClientOptions
    {
        Transport = transport,
        Retry =
        {
            Delay = TimeSpan.Zero,
            MaxRetries = 1,
            Mode = RetryMode.Fixed
        }
    };
    var client = new BlobClient(
        new Uri("https://storage.test/container/blob"),
        new AzureSasCredential("sig=test"),
        options);

    var response = await client.ExistsAsync(TestContext.Current.CancellationToken);

    Assert.True(response.Value);
    Assert.Equal(2, transport.CallCount);
    Assert.Equal(0, transport.RemainingResponseCount);
    transport.VerifyCalled(RequestMethod.Head, "/container/blob?sig=test", expectedCount: 2);
}
```
<!-- end-snippet -->

The transport records method, URI, headers, and buffered content. Use `VerifyCalled` for an exact
method and URI, or `Verify` with a predicate, expected count, and description. An unexpected call
returns a diagnostic `501` response; response exhaustion exposes an unplanned retry.

## Failure verification and secret safety

Verification output redacts SAS and other sensitive query values and excludes header values and
request bodies. The recorded request remains available for exact in-memory assertions:

<!-- snippet: tests/XBullet.EasyTesting.Tests/AzureTestingTests.cs#docs-azure-diagnostics -->
```csharp
[Fact]
public async Task Failed_verification_redacts_actual_and_expected_SAS_query_values()
{
    var transport = new StubAzureHttpPipelineTransport().Respond();
    var client = CreateSasBlobClient(transport);

    await client.ExistsAsync(TestContext.Current.CancellationToken);

    var exactFailure = Assert.Throws<AzureTransportVerificationException>(() =>
        transport.VerifyCalled(
            RequestMethod.Head,
            "https://other.test/expected?sig=expected-signature&comp=properties"));
    var predicateFailure = Assert.Throws<AzureTransportVerificationException>(() =>
        transport.Verify(_ => false, expectedCount: 1, "the expected blob request"));

    Assert.DoesNotContain(SasSignature, exactFailure.Message);
    Assert.DoesNotContain("expected-signature", exactFailure.Message);
    Assert.DoesNotContain("https://storage.test", exactFailure.Message);
    Assert.DoesNotContain("https://other.test", exactFailure.Message);
    Assert.Contains(
        "/expected?sig={Redacted}&comp=properties",
        exactFailure.Message);
    Assert.Contains("/container/blob?comp=metadata", exactFailure.Message);
    Assert.Contains("sv={Redacted}", exactFailure.Message);
    Assert.Contains("sp={Redacted}", exactFailure.Message);
    Assert.Contains("comp=metadata", predicateFailure.Message);
    Assert.DoesNotContain(SasSignature, predicateFailure.Message);
}
```
<!-- end-snippet -->

Never serialize raw recorded requests into logs or snapshots without application-specific
redaction. Keep synthetic tokens and SAS signatures in tests. Pipeline tests prove client behavior
against arranged HTTP contracts; use an emulator or isolated Azure resource when service-side
authorization, consistency, or semantics are the subject.

`TestTokenCredential` and `StubAzureHttpPipelineTransport` implement scenario-resource cleanup.
Register shared instances so request history and queued responses do not cross scenario boundaries.
See [`AzureTestingTests`](../../tests/XBullet.EasyTesting.Tests/AzureTestingTests.cs) for validation,
reset, cancellation, unarranged response, and SAS matching cases.

Browse the [Azure API reference](../api/packages/xbullet-easytesting-azure.md).
