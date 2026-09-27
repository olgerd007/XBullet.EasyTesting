# Documentation release checklist

Use this checklist for stable and preview releases. Complete it on the release commit before
creating the semantic-version tag.

## Content

- [ ] `CHANGELOG.md` describes user-visible changes and links to relevant guides, migration notes,
      and API package pages.
- [ ] Breaking or behavior-changing releases include migration guidance.
- [ ] New public APIs have complete XML comments and an approved public API entry.
- [ ] Package READMEs contain a minimal current example and the stable API-reference link.
- [ ] `docs/documentation-coverage.md` reflects new, changed, or removed functionality.
- [ ] Review dates in `documentation-maintenance.md` are updated for every area reviewed during the
      release.

## Verification

- [ ] The Release build and complete non-explicit test suite pass.
- [ ] `./eng/verify-documentation.ps1 -NoBuild -WarningsAsErrors` passes.
- [ ] The manual external-link workflow has no permanent failures; transient warnings were
      inspected.
- [ ] Important generated API pages show parameters, return values, exceptions, source links, and
      their conceptual guide links.
- [ ] The local site under `artifacts/docs` has working package and namespace navigation and search.

## Publication

- [ ] The release tag and GitHub release type agree: preview tags are pre-releases and stable tags
      are stable releases.
- [ ] The release notes link each major feature to its guide and API package page.
- [ ] NuGet package artifacts contain their intended package README and XML documentation file.
- [ ] The documentation workflow for the tag succeeds and the GitHub Pages deployment reports the
      tagged source revision.
- [ ] Published package and documentation links are checked after deployment.
