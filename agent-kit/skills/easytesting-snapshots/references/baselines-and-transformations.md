# Baselines and transformations

## Stabilize only values outside the contract

Use `SnapshotSettings` for assertion-time transformations and naming. Prefer a targeted JSON path
when the same member name appears in multiple contexts. Member rules are recursive and
case-insensitive; an overly broad `ScrubMember("id")` can conceal a meaningful identifier mismatch.

- `ScrubPath` or `ScrubMembers`: keep the selected member but replace its value.
- `IgnorePath` or `IgnoreMembers`: remove the selected member.
- `ScrubGuids` and `ScrubDateTimes`: stabilize matching JSON string values.
- `ScrubbingUrlPathGuids`: stabilize GUID segments in JSON `Url` members at assertion time.
- `SortArray`: use only when array order is outside the API contract.
- `ForVariant` or `ForHashedVariant`: distinguish parameterized cases or multiple assertions.

Keep secrets out of both verified files and received diagnostics. Redact authentication, cookies,
API keys, sensitive query parameters, and body fields as appropriate. Default header/query
redaction cannot identify all business secrets. Prefer synthetic data and narrow capture settings.

Configure global templates once before parallel execution, then use independent per-test copies.
Do not mutate static defaults during concurrent tests. Capture-option defaults and snapshot-setting
defaults are distinct; inspect the installed `...Defaults` APIs before composing them.

## Review and acceptance

Normal tests verify existing baselines. Missing or mismatched snapshots produce received files
and failures for review. In multi-target tests, received filenames include the target framework.
Inspect the full diff, confirm the intended behavior, and accept only the relevant received file
using the documented workflow. Review generated values and sensitive content before committing.

Use `SnapshotUpdateMode.Missing` only for deliberate initial baseline creation and `All` only for
an explicitly intended replacement. Keep ordinary CI in `None` mode. Do not set update environment
variables globally or add `AllowingUpdatesInContinuousIntegration()` to normal contract tests.
Keep `*.received.*` ignored, and commit only reviewed `*.verified.*` files.

## Verify.Xunit adapter

Keep Verify's approval workflow when the project already uses `XBullet.EasyTesting.Verify.Xunit`.
Import `XBullet.EasyTesting.Verify.Xunit` and use `VerifyControllerSnapshot` or
`VerifyHttpExchangeSnapshot`. Capture options still belong to the controller or exchange capture
configuration; Verify naming and scrubbing use `VerifySettings`, not built-in `SnapshotSettings`.
Cancellation covers capture; it does not independently cancel Verify once its model is created.

- [Snapshot stabilization](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/stabilizing-data.md)
- [Files and variants](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/files-and-naming.md)
- [Baseline maintenance](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/snapshots/maintenance.md)
- [Verify integration](https://github.com/olgerd007/XBullet.EasyTesting/blob/main/docs/guides/verify-xunit.md)

These links track `main`; use documentation matching the consuming project's package release.
