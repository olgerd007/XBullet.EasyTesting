# Assert controller responses

`TestScenarioResult.Should()` returns framework-independent response assertions for status, success,
headers, and structural JSON bodies. Use them when a test should report the HTTP contract in terms
of expected and actual values without depending on an assertion-library adapter.

## Assert status and headers

<!-- snippet: tests/TestApi.IntegrationTests/TestHttpResponseAssertionsTests.cs#docs-response-assertions -->
```csharp
[Fact]
public async Task Response_assertions_accept_status_and_response_or_content_headers()
{
    using var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
    };
    response.Headers.Add("X-Correlation-Id", "test-42");
    using var result = await ExecuteAsync(response);

    var assertions = result.Should();

    Assert.Same(assertions, assertions
        .HaveStatusCode(HttpStatusCode.OK)
        .BeSuccessful()
        .HaveHeader("X-Correlation-Id")
        .HaveHeader("X-Correlation-Id", "test-42")
        .HaveHeader("Content-Type"));
}
```
<!-- end-snippet -->

`HaveHeader` searches both response headers and content headers. Use the value overload when the
exact header value is part of the contract. Avoid asserting volatile transport headers unless the
application intentionally controls them.

`BeSuccessful` accepts status codes in the successful HTTP range. Use `HaveStatusCode` when one
specific status is required.

## Assert a JSON body

`HaveJsonBodyAsync` serializes the expected value and compares JSON structurally. Property order and
insignificant whitespace do not affect the result. Pass `JsonSerializerOptions` when application
naming, converters, or number handling differs from defaults.

```csharp
await result.Should()
    .HaveStatusCode(HttpStatusCode.OK)
    .HaveJsonBodyAsync(
        new OrderResponse(42, "Ready"),
        cancellationToken: cancellationToken);
```

Invalid response JSON produces `TestHttpResponseVerificationException` with the parse failure as
its inner exception. A structural mismatch includes formatted expected and actual JSON. Keep bodies
small enough for useful diagnostics; use snapshots when the whole response is intentionally a
reviewed artifact.

## Understand failure diagnostics

Assertions throw `TestHttpResponseVerificationException`. Stable messages name the assertion and
include the expected and actual status, header, or JSON content:

<!-- snippet: tests/TestApi.IntegrationTests/TestHttpResponseAssertionsTests.cs#docs-response-assertion-failures -->
```csharp
[Fact]
public async Task Response_assertions_describe_status_and_header_failures()
{
    using var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
    response.Headers.Add("X-Mode", "actual");
    using var result = await ExecuteAsync(response);
    var assertions = result.Should();

    var status = Assert.Throws<TestHttpResponseVerificationException>(
        () => assertions.HaveStatusCode(HttpStatusCode.OK));
    var success = Assert.Throws<TestHttpResponseVerificationException>(
        () => assertions.BeSuccessful());
    var missing = Assert.Throws<TestHttpResponseVerificationException>(
        () => assertions.HaveHeader("X-Missing"));
    var missingWithValue = Assert.Throws<TestHttpResponseVerificationException>(
        () => assertions.HaveHeader("X-Missing", "expected"));
    var wrongValue = Assert.Throws<TestHttpResponseVerificationException>(
        () => assertions.HaveHeader("X-Mode", "expected"));

    Assert.Contains("200 (OK)", status.Message);
    Assert.Contains("400 (BadRequest)", status.Message);
    Assert.Contains("successful", success.Message);
    Assert.Contains("X-Missing", missing.Message);
    Assert.Contains("X-Missing", missingWithValue.Message);
    Assert.Contains("expected", wrongValue.Message);
    Assert.Contains("actual", wrongValue.Message);
    Assert.Throws<ArgumentException>(() => assertions.HaveHeader(" "));
    Assert.Throws<ArgumentNullException>(() => assertions.HaveHeader("X-Mode", null!));
}
```
<!-- end-snippet -->

Tests of diagnostics should assert stable fields rather than the complete message. This allows
wording and formatting to improve without breaking consumers that do not depend on exact text.

## Ownership and cancellation

The scenario result owns the client and response produced by `ExecuteAsync`; dispose it after the
last assertion. Direct-client tests continue to own and dispose their client and response
separately. Pass the test cancellation token to request and asynchronous body assertion methods.

## When to use snapshots

Use direct response assertions for focused behavioral contracts and snapshots for larger payloads
whose changes should be reviewed as a whole. Snapshot options provide redaction, scrubbers, update
modes, and obsolete-file detection. See [built-in snapshot testing](snapshots.md).

## Related documentation

- [Run your first controller test](../getting-started/first-controller-test.md)
- [Built-in snapshot testing](snapshots.md)
- [Verify.Xunit controller snapshots](verify-xunit.md)
- [Core API reference](../api/packages/xbullet-easytesting.md)
