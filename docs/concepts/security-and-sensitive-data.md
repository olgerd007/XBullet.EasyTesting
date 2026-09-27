# Protect sensitive test data

Integration tests observe requests, messages, telemetry, database state, and application failures.
Those surfaces can contain credentials or personal data even when the assertion itself is harmless.
Treat every captured value as sensitive until the test proves otherwise.

This guidance applies to local runs, CI logs, received snapshot files, verified snapshots, test
results, and diagnostics attached to exceptions.

## Use a layered safety model

Apply these controls in order:

1. Use synthetic identities, tokens, API keys, signatures, and business data.
2. Capture only the fields needed to prove the behavior.
3. Exclude values that have no place in diagnostics or snapshots.
4. Redact values whose structure matters but whose contents must not persist.
5. Review the final serialized artifact before committing or publishing it.

Redaction is a last boundary, not permission to use production secrets. A newly introduced header,
query parameter, payload property, telemetry tag, or serializer can bypass an existing rule.

## Know where values can escape

| Surface | Values at risk | Required practice |
| --- | --- | --- |
| Authentication | Bearer tokens, API keys, certificates, claims, cookies | Use synthetic credentials and never include raw credential values in assertion messages |
| Outbound HTTP | Authorization headers, cookies, query tokens, request bodies | Assert raw recorder state only in memory; redact application-specific values before logging or snapshotting |
| Azure SDK transport | SAS signatures, credentials, headers, content | Rely on safe failure summaries, keep recorded requests in memory, and use isolated resources for service-side authorization tests |
| Messaging | Business payloads, destination headers, correlation metadata | Publish synthetic payloads and assert only fields needed by the contract |
| Observability | Log state, trace tags, baggage, events, metric dimensions | Configure the application not to emit secrets and avoid serializing an entire collector |
| Snapshots | Any serialized request, response, object graph, or diagnostic | Exclude or redact before comparison, then inspect every received file before acceptance |
| Test infrastructure | Connection strings, container credentials, environment variables | Keep secrets in the test process, avoid command output, and dispose scenario-owned resources |

Default redaction covers only documented common names. It cannot identify domain-specific secrets
such as `customerKey`, `privateCode`, or a token embedded in a JSON body. Add explicit rules at the
broadest safe scope and keep a focused test that proves the value is absent from failure output.

## Preserve useful diagnostics safely

A safe failure still needs enough context to explain the mismatch. Prefer method, normalized path,
status, count, field name, and predicate description over complete serialized objects. Replace a
sensitive value with a stable marker rather than removing the surrounding structure when that
structure helps diagnose the test.

Recorders intentionally retain some raw values for exact in-memory assertions. This does not make
them safe to serialize. Do not write a recorder, exception `Data` dictionary, telemetry collector,
or dependency response to console output without reviewing its contents.

## Review snapshots and artifacts

Before accepting or publishing a generated artifact:

1. Search it for authentication headers, cookies, tokens, keys, signatures, connection strings,
   personal data, and environment-specific hosts.
2. Confirm that a redacted value cannot be reconstructed from another captured field.
3. Check both the expected and actual sides of failure diagnostics.
4. Delete unneeded received files and test-result attachments.
5. Rotate the credential immediately if a real secret was exposed; removing it from the latest
   commit is not sufficient once it has entered history or logs.

The repository ignores ordinary received snapshot files, but ignore rules are not a security
control. Verified files are committed intentionally and therefore require human review.

## Package-specific controls

- [Authentication models](authentication.md) explains simulated and end-to-end credential
  boundaries.
- [Outbound HTTP](../guides/outbound-http.md#verification-and-failure-diagnostics) describes safe
  mismatch output and the raw request recorder.
- [Azure SDK testing](../guides/azure-sdk.md#failure-verification-and-secret-safety) documents SAS
  and request diagnostics.
- [Observability](../guides/observability.md) covers captured logs, traces, and metrics.
- [Stabilize snapshot data](../guides/snapshots/stabilizing-data.md) covers exclusions, redaction,
  scrubbing, and hashing.
- [Snapshot maintenance](../guides/snapshots/maintenance.md) defines the review and acceptance
  workflow.

Follow [multi-target and parallel execution](multi-target-and-parallel-execution.md) when captured
state or artifacts could be shared by concurrent test runs.
