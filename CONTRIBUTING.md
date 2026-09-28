# Contributing

Thank you for helping improve XBullet.EasyTesting.

## Development setup

Install the .NET SDK selected by `global.json`, clone the repository, and run:

```shell
dotnet restore XBullet.EasyTesting.sln
dotnet build XBullet.EasyTesting.sln --configuration Release --no-restore
dotnet test XBullet.EasyTesting.sln --configuration Release --no-build
```

The solution build runs the strict documentation checks once after the library projects finish. For
a source-only iteration, opt out explicitly with
`-p:VerifyDocumentationOnBuild=false`; run an ordinary build again before opening a pull request.

Before opening a pull request, also verify formatting:

```shell
dotnet format XBullet.EasyTesting.sln --verify-no-changes --no-restore
```

The root `.editorconfig` is the source of truth for formatting, naming, and C# style. Stable rules
are build warnings (and therefore errors in this repository); preference-heavy modern C# rules are
IDE suggestions. Member accessibility ordering is intentionally left to code review so related API
members can remain cohesive and field-initializer order is preserved.

To run the same documentation gate directly without rebuilding the solution:

```powershell
./eng/verify-documentation.ps1 -NoBuild -WarningsAsErrors
```

This verifies Markdown style, executable snippets, XML comments, generated API pages, and internal
links. The generated site is written to `artifacts/docs`; generated metadata and HTML are not
committed. See the
[documentation maintenance policy](docs/contributing/documentation-maintenance.md) for external
link handling and review intervals.

Check all direct and transitive NuGet dependencies for known vulnerabilities:

```shell
dotnet list XBullet.EasyTesting.sln package --vulnerable --include-transitive
```

Restore fails when NuGet reports a moderate, high, or critical vulnerability. Builds also run all
enabled-by-default .NET security analyzers.

## Load testing

Run the concurrent HTTP-stub workload in Release mode when changing request capture or rule matching:

```shell
dotnet run --project tests/XBullet.EasyTesting.LoadTests -c Release
```

Use `--requests`, `--concurrency`, and `--rules` after `--` to compare the same workload before
and after a change. The harness reports throughput, latency percentiles, errors, and allocations.

## Pull requests

- Keep changes focused and include tests for externally observable behavior.
- Preserve existing public APIs unless the change intentionally introduces a documented breaking
  change.
- Update README examples when a public workflow changes.
- Do not commit `*.received.*`, `bin`, `obj`, test results, coverage, or package artifacts.
- Commit reviewed `*.verified.*` snapshot files when their change is intentional.

## Releases

Every CI run creates downloadable `VersionPrefix-preview.<run number>` packages without publishing
them. Package versions published to NuGet.org are derived from GitHub Release tags. Maintainers
should update `CHANGELOG.md`, create a tag such as `v0.2.0`, and publish the corresponding GitHub
Release. Public previews use a tag such as `v0.2.0-preview.1` and must be marked as a GitHub
pre-release. The release workflow validates, packs, and publishes every project under `src` to
NuGet.org.

Complete the [documentation release checklist](docs/contributing/release-checklist.md) before
creating a release tag.
