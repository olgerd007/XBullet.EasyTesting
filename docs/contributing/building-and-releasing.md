# Build and release

This guide describes local validation, explicit infrastructure tests, preview packages, and stable or preview releases.

## Building and releasing

Build and test the complete solution locally:

```shell
dotnet restore XBullet.EasyTesting.sln
dotnet build XBullet.EasyTesting.sln --configuration Release --no-restore
dotnet test XBullet.EasyTesting.sln --configuration Release --no-build
```

The solution build also verifies Markdown, synchronized snippets, public XML comments, generated
API pages, and internal links. Use `-p:VerifyDocumentationOnBuild=false` only for a source-only
iteration, then run the ordinary build again before submitting or releasing the change.

Tests that start Aspire processes or Docker containers are explicit. Run the real dependency suite
separately when validating infrastructure changes:

```shell
dotnet test tests/XBullet.EasyTesting.ContainerTests/XBullet.EasyTesting.ContainerTests.csproj \
  --configuration Release --explicit only
```

The regular CI matrix compiles these projects but skips their explicit runtime tests to keep feedback
fast; run them locally or from a manually provisioned environment when changing infrastructure support.

GitHub Actions builds and tests on Windows and Ubuntu, records Cobertura code coverage, validates
public API approvals and XML comments, builds the searchable reference with warnings treated as
errors, checks package compatibility against the latest stable release, and creates packages for
pushes and pull requests.

The documentation workflow publishes `artifacts/docs` to GitHub Pages from `main` and semantic
version tags. A tag therefore publishes the reference generated from the same source revision as
the corresponding package release. The local solution build already applies the strict generated
documentation gate; before a release, inspect important entry-point pages under `artifacts/docs`.
See [API reference generation](api-reference.md) for the committed inputs and exclusion policy.

Before creating a tag, complete the
[documentation release checklist](release-checklist.md). It covers changelog links, migration
guidance, API pages, generated-site inspection, and post-deployment verification.

Publishing uses NuGet.org trusted publishing instead of a long-lived API key. Configure a GitHub
trusted publisher for the `olgerd007/XBullet.EasyTesting` repository and
`.github/workflows/publish-nuget.yml`, update `CHANGELOG.md`, and publish a GitHub Release with a
semantic-version tag such as `v1.2.3`. The release workflow exchanges its GitHub OIDC token for a
short-lived NuGet API key, then publishes all `XBullet.EasyTesting.*` packages and their symbol
packages.

Public API approval files live beside each package project. New intentional APIs belong in
`PublicAPI.Unshipped.txt`; move them to `PublicAPI.Shipped.txt` when preparing a stable release.
Unapproved public changes and binary compatibility breaks fail the build or package step.

### Preview flow

Every CI run creates preview packages using the current `VersionPrefix` and the workflow run number,
for example `1.0.4-preview.42`. Download the `nuget-preview-42` workflow artifact and use its
directory as a local NuGet source to test the complete package set without publishing it.

To publish a public preview to NuGet.org, create a GitHub Release with a tag such as
`v1.0.4-preview.1` and select **Set as a pre-release**. The release workflow verifies that the
GitHub release type and semantic version agree before publishing. Install public previews with:

```shell
dotnet add package XBullet.EasyTesting --prerelease
```

For a stable release, use a tag without a suffix, such as `v1.0.4`, and do not mark the GitHub Release as a pre-release.

See the [contribution guidelines](../../CONTRIBUTING.md) and
[security policy](../../SECURITY.md) for private vulnerability reporting. Documentation owners and
review intervals are defined in [documentation maintenance](documentation-maintenance.md).
