# NuGet Preview GitHub Release Checklist

This checklist is for publishing the NuGet preview packages from a GitHub
Release. It is not the later code/repository release checklist.

Use it before publishing a GitHub Release because `release.published` is the
NuGet publish trigger for `Sekiban.Dcb.WasmRuntime`,
`Sekiban.Dcb.WasmRuntime.Remote`, and `Sekiban.Dcb.WasmRuntime.Wasmtime`.

## Release Identity

- [ ] The GitHub Release is created in `J-Tech-Japan/SekibanWasmRuntime`.
- [ ] The tag is `v1.0.0-preview.<n>` or `1.0.0-preview.<n>`.
- [ ] The normalized tag value is the intended NuGet `PackageVersion` for all
  public preview packages.
- [ ] `Directory.Build.props` remains on the active `1.0.0-preview.*` version
  line.
- [ ] The release is marked as a prerelease in GitHub.

## Release Notes

- [ ] Start from `.github/release-notes/nuget-preview.md`.
- [ ] Copy the operator-relevant summary from `CHANGELOG.md`.
- [ ] Link `docs/release/migration-notes.md` when the release includes breaking
  public contract changes.
- [ ] List known preview limitations that affect package selection, runtime
  deployment, or upgrade safety.
- [ ] Link the dry-run evidence pack and any refreshed compatibility evidence.

## Required Evidence

- [ ] `reports/public-release/preview-release-dry-run.md` exists for the package
  version being released.
- [ ] The dry run used the intended package version:

  ```bash
  PACKAGE_VERSION=1.0.0-preview.<n> scripts/release/dry-run-preview-readiness.sh
  ```

- [ ] The evidence pack records package inspection, secret scan,
  consumer smoke, license/notice check, public hygiene, artifact inventory,
  serialized DCB contract evidence, and whitespace validation.
- [ ] `reports/compatibility/serialized-dcb-contract-black-box-baseline.md` is
  current when serialized public contracts changed.
- [ ] `reports/public-release/release-publish-gate.md` still matches the
  workflow behavior.
- [ ] `git diff --check` passes before the release PR is merged.

## Safe No-Publish Verification

- [ ] Pull request validation for `.github/workflows/release-nuget-preview.yml`
  completed successfully and did not publish.
- [ ] Optional manual workflow dry run completed with `workflow_dispatch` and an
  explicit `package_version`, and did not publish.
- [ ] No GitHub Release was published during verification.
- [ ] No `dotnet nuget push` command was run locally.

## Publish Guard

- [ ] The protected `nuget-preview` environment is configured.
- [ ] `reports/public-release/nuget-environment-credential-preflight.md`
  confirms whether the environment and credential metadata were checked
  automatically or require manual operator confirmation.
- [ ] The environment approval is granted only after the release notes and
  evidence links have been reviewed.
- [ ] `NUGET_API_KEY` is configured in the `nuget-preview` environment.
- [ ] Missing or unverified `nuget-preview` environment configuration is
  treated as release-blocking.
- [ ] Missing or unverified `NUGET_API_KEY` configuration is treated as
  release-blocking.
- [ ] Operators understand that a missing `NUGET_API_KEY` fails a real
  `release.published` publish attempt before `dotnet nuget push`.

## Final Publish Step

- [ ] Confirm the release tag, release title, and package version agree.
- [ ] Confirm the GitHub Release body uses the NuGet preview template.
- [ ] Publish the GitHub Release only after all checklist items above are true.
- [ ] Confirm the `release-nuget-preview` workflow readiness job passed.
- [ ] Confirm the publish job pushed the intended packages. A missing NuGet
  credential is a failed publish attempt, not a successful skip.
