# [Verb-led feature title]

<!--
Use this template for a task-oriented feature guide. Remove comments and sections that genuinely do
not apply before publishing. Follow docs/documentation-style-guide.md.
-->

[State the result the reader will achieve and the situation in which this feature is appropriate.]

## Prerequisites

- .NET [supported version or versions]
- `[required package]`
- [application or runtime prerequisite]

```shell
dotnet add package [Package.Id]
```

## Minimal example

<!-- Use a verified snippet from canonical executable source. -->

```csharp
[Complete, focused arrange/act/assert example]
```

[Explain the calls that determine behavior. Do not narrate self-evident syntax.]

## Configure [important behavior]

[Show realistic variants, defaults, and when to choose each option.]

```csharp
[Verified configuration example]
```

## Combine with [related capability]

[Show cross-package composition only when it represents a common workflow.]

## Diagnose failures

[Describe the failure boundary, public exception or diagnostic type, stable diagnostic fields, and
the corrective action. Include a negative example where useful.]

## Limitations and safety

- [Runtime or emulation boundary]
- [Lifecycle, cleanup, parallelism, or cancellation constraint]
- [Security or sensitive-data consideration]
- [Alternative to use when this feature is not appropriate]

## Related documentation

- Related guide: `[replace with a repository-relative link]`
- API reference: `[replace with the generated API link]`
- Canonical executable example: `[replace with a repository-relative source link]`
