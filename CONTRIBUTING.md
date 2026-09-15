# Contributing

Thank you for helping improve XBullet.EasyTesting.

## Development setup

Install the .NET SDK selected by `global.json`, clone the repository, and run:

```shell
dotnet restore XBullet.EasyTesting.sln
dotnet build XBullet.EasyTesting.sln --configuration Release --no-restore
dotnet test XBullet.EasyTesting.sln --configuration Release --no-build
```

Before opening a pull request, also verify formatting:

```shell
dotnet format XBullet.EasyTesting.sln --verify-no-changes --no-restore
```

## Pull requests

- Keep changes focused and include tests for externally observable behavior.
- Preserve existing public APIs unless the change intentionally introduces a documented breaking change.
- Update README examples when a public workflow changes.
- Do not commit `*.received.*`, `bin`, `obj`, test results, coverage, or package artifacts.
- Commit reviewed `*.verified.*` snapshot files when their change is intentional.

## Releases

Every CI run creates downloadable `VersionPrefix-preview.<run number>` packages without publishing them. Package versions published to NuGet.org are derived from GitHub Release tags. Maintainers should update `CHANGELOG.md`, create a tag such as `v0.2.0`, and publish the corresponding GitHub Release. Public previews use a tag such as `v0.2.0-preview.1` and must be marked as a GitHub pre-release. The release workflow validates, packs, and publishes every project under `src` to NuGet.org.
