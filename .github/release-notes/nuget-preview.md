# SekibanWasmRuntime NuGet Preview 1.0.0-preview.1

Use this body for the GitHub Release that publishes the NuGet preview packages.
This is the NuGet package release note source, not the later code/repository
release note source.

> Release blocker: do not publish the GitHub Release until an operator confirms
> the `nuget-preview` protected environment exists and contains an environment
> secret named `NUGET_API_KEY`. The automated metadata preflight could not verify
> those settings from this checkout.

## Packages

- `Sekiban.Dcb.WasmRuntime` `1.0.0-preview.1`
- `Sekiban.Dcb.WasmRuntime.Remote` `1.0.0-preview.1`
- `Sekiban.Dcb.WasmRuntime.Wasmtime` `1.0.0-preview.1`

## Highlights

- Establishes the initial public preview package baseline for shared runtime
  contracts, serialized DCB command/query DTOs, remote HTTP client support, and
  in-process Wasmtime projection hosting.
- Validates the package matrix with pack inspection, high-confidence secret
  scanning, license and notice checks, public hygiene checks, artifact
  inventory, a generated local NuGet consumer smoke, and the serialized DCB
  contract black-box baseline.
- Publishes through the GitHub Release driven NuGet gate only after the
  `nuget-preview` environment approval and `NUGET_API_KEY` credential are
  confirmed.

## Compatibility And Migration

- Migration notes: `docs/release/migration-notes.md` states that no breaking
  public contract change is introduced by the release-process documentation
  update.
- Compatibility evidence:
  `reports/compatibility/serialized-dcb-contract-black-box-baseline.md` records
  a passing runtime-owned serialized DCB command, query, tag-state, and
  compatibility baseline for `1.0.0-preview.1`.

## Preview Limitations

- These packages are prerelease packages on the `1.0.0-preview.*` version line.
  Consumers must enable prerelease package resolution.
- Most applications should install only the package for their runtime boundary:
  core shared contracts, remote HTTP client support, or Wasmtime in-process
  hosting.
- `Sekiban.Dcb.WasmRuntime.Wasmtime` is preview-only while native asset
  packaging and host policy are finalized. The current macOS inspection includes
  `libwasmtime.dylib`; Linux and Windows package candidates require their own
  release-environment inspection before platform-specific publication claims.
- The Elastic License 2.0 hosted-service restriction remains in force. This
  preview does not grant permission to provide SekibanWasmRuntime as a hosted,
  managed, SaaS, or similar third-party service without a separate commercial
  license.
- The NuGet package release happens before the later code/repository release.
  Do not treat this GitHub Release as the final source-code release milestone.

## Release Evidence

- Dry-run evidence: `reports/public-release/preview-release-dry-run.md`
  records a `1.0.0-preview.1` PASS with WARN for the dry-run-only missing
  `NUGET_API_KEY` condition.
- Publish gate: `reports/public-release/release-publish-gate.md`.
- Environment and credential preflight:
  `reports/public-release/nuget-environment-credential-preflight.md`.
- Release notes closeout:
  `reports/public-release/preview-release-notes-closeout.md`.
- Checklist: `docs/release/nuget-preview-release-checklist.md`.

## Operator Confirmation

- Tag/package version: `1.0.0-preview.1`
- Protected environment: `nuget-preview`
- Dry-run command:

  ```bash
  PACKAGE_VERSION=1.0.0-preview.1 scripts/release/dry-run-preview-readiness.sh
  ```
- NuGet release is the first public release milestone; the later
  code/repository release checklist is separate.
