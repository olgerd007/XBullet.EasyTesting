---
name: easytesting-snapshots
description: >-
  Create, diagnose, or review XBullet.EasyTesting JSON, text, controller, and HTTP exchange
  snapshots, including Newtonsoft.Json wire contracts, recorder capture, redaction, and baseline
  changes. Use for built-in snapshot assertions or the framework's Verify.Xunit adapters.
---

# XBullet snapshot contracts

## Choose the contract and engine

Inspect the installed snapshot packages, existing verified files, application serializer, and
current test engine. Preserve the established built-in or Verify.Xunit workflow. Use
`XBullet.EasyTesting.Snapshots.Core` for built-in values, body, controller, and direct or recorded
HTTP exchanges. Add `XBullet.EasyTesting.Snapshots.Http` for outbound stub snapshots or the fluent
`SnapshotScenario` adapter. Keep the combined `XBullet.EasyTesting.Snapshots` facade for existing
consumers; new tests can use focused packages. Snapshot extension namespaces are
`XBullet.EasyTesting.Snapshots`, even for the `.Snapshots.Http` package.

| Contract | Built-in API |
| --- | --- |
| C# object serialized with System.Text.Json | `SnapshotAssert.MatchAsync` |
| Already serialized JSON | `SnapshotAssert.MatchJsonAsync` |
| Text | `SnapshotAssert.MatchTextAsync` |
| Actual HTTP JSON body | `response.ShouldMatchJsonBodySnapshot` |
| Controller response and selected metadata | `response.ShouldMatchControllerSnapshot` |
| Request/response exchange | `response.ShouldMatchHttpExchangeSnapshot` |
| Recorder's completed exchanges | `recorder.ShouldMatchHttpExchangesSnapshot` |

## Preserve application serialization

HTTP body and exchange capture parse actual content into a JSON tree. They preserve the names,
types, omitted fields, explicit nulls, and custom-converter output already emitted by the
application, including Newtonsoft.Json output, unless configured transformations change them.
They do not deserialize the body into an application DTO. Formatting and escaping can differ;
these are structural contracts, not byte-identical network captures.

`MatchAsync(dto)` instead uses System.Text.Json and does not inherit MVC Newtonsoft.Json settings
or attributes. Do not deserialize a response into a DTO and snapshot that DTO when the intended
contract is the real API JSON. The outer exchange model is formatted by the snapshot engine.

## Capture complete exchanges correctly

Read [exchange capture](references/exchange-capture.md) when request content, recorders, streaming,
or capture options matter. Choose direct capture only when the necessary request content remains
available. Use pre-transport recording for a complete request-body contract. Configure capture
options at recorder creation; configure snapshot transformations during assertion.

## Stabilize and review

Read [baselines and transformations](references/baselines-and-transformations.md) for redaction,
volatile data, naming, updates, and Verify integration. Preserve meaningful business values and
array ordering. A failing snapshot may signal a regression; inspect the received-versus-verified
diff before changing a baseline. Do not accept all files or enable CI updates to conceal a failure.

Run affected tests with verification mode enabled. Report intentional baseline changes and the
specific contract they describe. Never commit received files, secrets, or unreviewed output.
