# Preview Release Notes And Changelog Closeout

Issue: `#153` / `SWR-G020 Preview release notes finalization and changelog closeout`

## Scope

This report records the release-note and changelog closeout for the intended
NuGet preview release. It does not create a GitHub Release and does not publish
NuGet packages.

- Intended package version: `1.0.0-preview.1`
- Release notes source: `.github/release-notes/nuget-preview.md`
- Changelog source: `CHANGELOG.md`
- NuGet release checklist: `docs/release/nuget-preview-release-checklist.md`

## Closeout Summary

| Area | Status | Evidence |
| --- | --- | --- |
| GitHub Release body | READY WITH MANUAL BLOCKER | `.github/release-notes/nuget-preview.md` contains concrete package versions, highlights, migration status, preview limitations, and evidence links for `1.0.0-preview.1`. |
| Changelog | READY WITH MANUAL BLOCKER | `CHANGELOG.md` contains a `1.0.0-preview.1` release-candidate entry and states the remaining protected-environment and publish-policy blocker before publication. |
| Migration status | READY | `docs/release/migration-notes.md` states that no breaking public contract change is introduced by the release-process documentation update. |
| Compatibility evidence | READY | `reports/compatibility/serialized-dcb-contract-black-box-baseline.md` records a passing serialized DCB contract baseline. |
| Dry-run evidence | READY | `reports/public-release/preview-release-dry-run.md` records `1.0.0-preview.1` readiness through the no-publish path; Trusted Publishing OIDC exchange is reserved for the real publish job. |
| Publish gate | MANUAL BLOCKER | `reports/public-release/nuget-environment-credential-preflight.md` could not verify `nuget-preview` metadata or NuGet.org Trusted Publishing policy state from this checkout; an operator must confirm both before publishing. |

## Operator Release Body Inputs

- Packages:
  - `Sekiban.Dcb.WasmRuntime` `1.0.0-preview.1`
  - `Sekiban.Dcb.WasmRuntime.Remote` `1.0.0-preview.1`
  - `Sekiban.Dcb.WasmRuntime.Wasmtime` `1.0.0-preview.1`
- Release type: NuGet preview package release only.
- Later code/repository release: separate milestone, not completed by this
  GitHub Release.
- License posture: Elastic License 2.0 hosted-service restriction remains
  unchanged.

## Release-Blocking Note

Do not publish the GitHub Release until an operator with repository settings
and NuGet.org package-owner access confirms:

1. `nuget-preview` exists as a protected GitHub Environment.
2. NuGet.org Trusted Publishing policy
   `SekibanWasmRuntime GitHub Release NuGet Preview` exists for package owner
   `J-Tech-Japan`, repository owner `J-Tech-Japan`, repository
   `SekibanWasmRuntime`, workflow file `release-nuget-preview.yml`, and
   environment `nuget-preview`.
3. If the policy is temporarily active, it is still within the 7-day activation
   window for the first successful publish.

If either setting is missing or still unverified, the release remains blocked.
