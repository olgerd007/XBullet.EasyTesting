# Documentation style guide

This guide defines how XBullet.EasyTesting documentation is organized and written. It applies to
the root README, package READMEs, detailed guides, code examples, and generated API reference.

## Audience and reading paths

Write first for a .NET developer who knows ASP.NET Core and xUnit but has not used
XBullet.EasyTesting. Do not assume that the reader knows the package layout or the difference
between simulated and end-to-end authentication.

Support three reading paths:

- A new user needs a short tutorial that produces a useful result.
- An existing user needs a task-oriented recipe and troubleshooting guidance.
- An API consumer needs exact behavior, defaults, parameters, return values, and exceptions.

Do not make one page serve all three purposes. Link between tutorials, guides, and reference pages.

## Canonical ownership

Keep each kind of information in one canonical location:

| Information | Canonical location | Other locations |
| --- | --- | --- |
| Project purpose and package selection | Root `README.md` | Link to the documentation home |
| NuGet installation and smallest package example | Package `README.md` | Repeat only when a guide needs setup context |
| Shared mental models and lifecycle | `docs/concepts/` | Summarize and link |
| Steps for accomplishing a task | `docs/guides/` | Link from package READMEs |
| Exact public API behavior | XML comments and generated reference | Link from guides |
| Executable example implementation | Canonical example or test source | Include a verified snippet or link |
| Release changes | `CHANGELOG.md` | Link to durable guides and migration pages |
| Current implementation roadmap | `docs/` roadmap file | Do not copy into user guides |

When short repetition is useful, keep it intentionally brief and link to the canonical page. Do
not maintain two long versions of the same guide.

## Page types

### Tutorial

A tutorial teaches a complete path in a deliberate order. State the result first, list
prerequisites, and end with a working test. Explain only the concepts required for that path.

### Concept

A concept page explains why the library behaves as it does. Use diagrams only when they make
lifecycle, ownership, or data flow materially clearer. Link to recipes instead of embedding every
variation.

### Guide

A guide solves one concrete problem. Use a goal-oriented title such as "Stub an outbound HTTP
request". Include setup, the smallest complete solution, important variants, failure behavior, and
related APIs.

### Reference

Reference pages describe public contracts precisely. Prefer generated reference pages backed by XML
comments. Reference content must state defaults, nullability, exceptions, side effects, lifecycle,
thread-safety, and ownership when relevant.

## Standard page structure

Detailed feature guides should normally use this order:

1. Title expressed as a user goal.
2. One-paragraph outcome and when to use the feature.
3. Prerequisites and package installation.
4. Minimal working example.
5. Explanation of the important calls.
6. Realistic variants or composition with other packages.
7. Failure behavior and diagnostics.
8. Limitations, security, lifecycle, and cleanup notes.
9. Related guides, API reference, and canonical tests.

Omit a section only when it genuinely does not apply. Do not add empty placeholder sections to
published pages.

## Voice and terminology

- Address the reader directly and use active voice.
- Lead with the result before implementation details.
- Prefer short sentences and concrete verbs: "create", "register", "send", "record", and
  "assert".
- Use "integration test" as a noun and "integration-test" as a compound adjective.
- Use "test host" for the host created by this library and `TestServer` for the ASP.NET Core type.
- Use "scenario" for the library-managed arrange/act/assert scope; use "test" for the xUnit test.
- Use "application under test" on first use. Use "application" afterward when unambiguous.
- Use "outbound HTTP" for calls made by the application and "controller response" for a response
  returned through the in-memory server.
- Use ".NET isolated worker" for the supported Azure Functions model.
- Write package, type, member, environment-variable, header, and file names as inline code.
- Use the exact public package name, including `.Core` or `.Http` suffixes.

Avoid "simple", "easy", "obvious", and "just". These words hide prerequisites and make failure
cases harder to understand.

## Headings and links

- Use one level-one heading per file and sentence case for headings.
- Do not skip heading levels.
- Make link text describe the destination; do not use "click here".
- Prefer repository-relative links for content in this repository.
- Link to a guide or type the first time it is central to a section; avoid linking every repeated
  occurrence.
- Link to stable source files rather than line numbers in durable documentation.
- Check fragment links whenever a heading changes.

## C# examples

Examples are product behavior and must be reviewable like code.

- Prefer executable examples sourced from a compiled test or example project.
- Show the complete arrange, act, assert, and cleanup lifecycle needed to understand the feature.
- Use `async Task` and pass a cancellation token when cancellation is relevant to the behavior.
- Use realistic domain names such as products, orders, and customers consistently.
- Use stable values such as `product-42`, `tenant-7`, and `orders.created` so related examples are
  easy to compare.
- Keep examples focused. Move unrelated application setup behind a clearly named fixture.
- Do not use `...` inside code presented as compilable.
- When omitting `using` directives or surrounding types, say so or make them available through the
  example project's implicit/global usings.
- Include expected diagnostic text only when it is a supported contract; otherwise describe the
  diagnostic and show its stable fields.
- Use xUnit v3 syntax for repository-owned tests.
- Dispose or asynchronously dispose owned clients, responses, hosts, scopes, and containers.
- Never place real credentials, tokens, connection strings, or personal data in an example.

If a Markdown snippet is copied from executable source, identify its source region in a comment or
in the documentation build configuration. The same code must not be maintained independently in
multiple Markdown files.

## Package README requirements

Every package README must contain:

- One sentence describing the testing problem it solves.
- Supported target frameworks.
- The exact `dotnet add package` command.
- A minimal example that demonstrates the package's primary value.
- Important runtime prerequisites and limitations.
- Links to the detailed package guide, API reference, and related packages.

Keep a package README suitable for display on NuGet. Advanced variations belong in detailed guides.

## Failures, limitations, and security

Do not document only the successful path. For every important workflow:

- Describe what fails and at which boundary.
- Name the exception or diagnostic type when it is part of the public contract.
- Show how to inspect relevant recorded calls, logs, or diagnostic data.
- State cleanup behavior after success, failure, and cancellation.
- Explain whether parallel execution is safe.
- Separate library emulation from production-runtime validation.
- Call out features that transport credentials without validating them.
- Redact authorization headers, cookies, API keys, access tokens, connection strings, and personal
  data in all output and snapshot examples.
- State external prerequisites such as Docker and required license acceptance.

## API comments

Public XML comments should use the following conventions:

- `<summary>` answers what the member does, without repeating its signature.
- `<remarks>` explains lifecycle, default behavior, ordering, thread-safety, or non-obvious tradeoffs.
- Every public method, constructor, delegate, indexer, and operator parameter has a matching
  `<param name="...">` entry, including parameters whose purpose appears obvious from the signature.
- Every public generic type or method parameter has a matching `<typeparam name="...">` entry.
- Parameter documentation explains the value's purpose, accepted values, default behavior,
  nullability, units, ownership, and side effects when applicable. Do not merely repeat the
  parameter name or type.
- Optional parameters explain what happens when the caller omits them. Configuration callbacks
  state what is being configured and whether `null` preserves defaults.
- `CancellationToken` parameters state which operation is cancelled and any observable behavior
  after cancellation.
- Delegate parameters describe when the delegate runs, what its arguments represent, and whether
  it may run more than once or concurrently.
- Every non-`void` public method and readable public property has useful return or value
  documentation. Use `<returns>` for methods and `<value>` for properties when the generated
  documentation benefits from information beyond the summary.
- Document exceptions callers are expected to handle or recognize.
- Use `<see cref="..."/>` for types and members and `<paramref name="..."/>` for parameters.
- Examples belong in guides unless a very short example materially clarifies a public contract.

For example:

```csharp
/// <summary>
/// Creates a client authenticated as a test user.
/// </summary>
/// <param name="configureUser">
/// Configures the identity sent with requests. When <see langword="null"/>, the client uses the
/// default authenticated test user.
/// </param>
/// <param name="cancellationToken">
/// Cancels client initialization. Cancellation does not dispose the owning test factory.
/// </param>
/// <returns>A client owned by the caller and configured for the test server.</returns>
public Task<HttpClient> CreateClientAsync(
    Action<TestUserBuilder>? configureUser = null,
    CancellationToken cancellationToken = default);
```

Generated-documentation warnings are defects unless the project explicitly suppresses a documented
compatibility or tooling case.

## Review checklist

- [ ] The page has one clear audience and purpose.
- [ ] The package and prerequisites are explicit.
- [ ] The example is executable and linked to canonical source.
- [ ] Defaults and important configuration choices are stated.
- [ ] Failure behavior, diagnostics, cleanup, and limitations are covered.
- [ ] Every public parameter and generic type parameter has useful XML documentation.
- [ ] Terminology matches this guide.
- [ ] Links are descriptive and repository-relative where possible.
- [ ] The page does not duplicate a longer canonical explanation.
- [ ] No secrets or sensitive values appear in examples or output.
