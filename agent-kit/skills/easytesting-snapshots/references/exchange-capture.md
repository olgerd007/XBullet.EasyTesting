# Exchange capture

## Direct capture

The following assumes `client` is a client without an `HttpExchangeRecorder` and the endpoint's
actual URI and body are supplied by the test:

```csharp
using var response = await client.GetAsync("/api/orders", cancellationToken);

await response.ShouldMatchHttpExchangeSnapshot(
    configureExchange: options => options.Response.IgnoringHeaders("Date"),
    configureSnapshot: settings => settings.ScrubPath("$.Response.Body.requestId"),
    cancellationToken: cancellationToken);
```

Import `XBullet.EasyTesting.Snapshots`. Direct capture reads `response.RequestMessage` and its
available content at assertion time. It cannot recover a request body that transport already
consumed or record a send failure that returned no response.

## Recorder-backed capture

Attach `HttpExchangeRecorder` to the real client pipeline before sending. It records the request
before transport and captures response content when the caller consumes it. The recorder observes
the transport; it is separate from `StubHttpMessageHandler`, which substitutes a remote boundary.

With `XBullet.EasyTesting.Snapshots.Http` installed, the fluent adapter can attach recording:

```csharp
using var result = await scope.SnapshotScenario(options =>
    {
        options.Request.ScrubbingUrlPathGuids();
        options.Response.IgnoringHeaders("Date");
    })
    .PostJson("/api/orders", requestBody)
    .ExecuteAsync(cancellationToken);

await result.Response.ShouldMatchHttpExchangeSnapshot(
    configureSnapshot: settings => settings.ScrubPath("$.Response.Body.requestId"),
    cancellationToken: cancellationToken);
```

Here `scope`, `requestBody`, and `cancellationToken` come from the consuming test. Adapt routes and
scrub paths to the actual response. `PostJson` uses the hosted System.Text.Json options, not
automatically Newtonsoft.Json. Recording captures the actual request serialization chosen by the
caller and the actual response serialization chosen by the app.

For a recorded response, omit assertion-time exchange options. A `configureExchange` callback or
separate `exchangeOptions` instance is rejected with `InvalidOperationException`, even when its
values match. The original options instance or `recorder.Options` is accepted, but does not
recapture an exchange. Use `configureSnapshot` or `snapshotSettings` to scrub captured data.

## Streaming and content classification

Normal buffered HttpClient operations consume the response before returning. With
`HttpCompletionOption.ResponseHeadersRead`, read the body fully before expecting a recorded body;
until then it is `{NotRead}`. Assertions on a recorded response reuse the captured exchange and
do not force an unread stream to complete. Body-read failures appear as `BodyFailure`, while send
failures are captured separately. Partial or failed content is not a complete success contract.

`application/json` and media types ending in `+json` are parsed as JSON. Text content is captured
as text and other content can become base64. Check content type and valid JSON when output looks
unexpected. The JSON parser is System.Text.Json, so nonstandard or malformed JSON is not guaranteed
to parse just because Newtonsoft.Json accepts it; direct JSON capture throws and recorded response
capture can fall back to base64 with a body failure.

Select JSON, HTTP transcript, or YAML through `HttpExchangeSnapshotOptions.Format` at the capture
configuration point. Each format represents the captured contract after configured transformations.

- [Exchange option ownership](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/recipes.md#choose-where-to-configure-exchange-options)
- [Real exchange capture](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/recipes.md#complete-testserver-exchanges)

These links track `main`; verify API availability against the installed package release.
