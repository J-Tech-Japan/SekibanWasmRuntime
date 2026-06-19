# Changelog

All notable public-preview changes for SekibanWasmRuntime are tracked here.

This repository is currently on the `1.0.0-preview.*` version line. See
[`docs/release/versioning-and-changelog.md`](docs/release/versioning-and-changelog.md)
for the version, changelog, migration-note, and compatibility evidence rules.

## Unreleased

- No additional consumer-facing package changes are staged after the
  `1.0.0-preview.1` release-candidate closeout below.

## 1.0.0-preview.1

- Release status: GitHub Release notes, changelog content, dry-run evidence,
  compatibility evidence, and publish-gate documentation are prepared for the
  NuGet preview release. The actual GitHub Release and NuGet publish remain
  blocked until an operator confirms the `nuget-preview` protected environment
  and its `NUGET_API_KEY` environment secret in repository settings.
- Established the initial public preview package baseline for:
  `Sekiban.Dcb.WasmRuntime`, `Sekiban.Dcb.WasmRuntime.Remote`, and
  `Sekiban.Dcb.WasmRuntime.Wasmtime`.
- Hardened the NuGet preview release workflow so readiness runs the serialized
  DCB contract baseline and real release publishes fail when `NUGET_API_KEY` is
  missing.
- Added a local NuGet consumer smoke that restores and builds a generated
  project against locally packed preview packages before publication.
- Added a NuGet preview GitHub Release checklist and release notes template for
  release operators.
- Documented the preview version, changelog, migration-note, and compatibility
  evidence source-of-truth for release operators.
- Clarified that breaking public contract changes require migration guidance,
  compatibility evidence, and GitHub Release notes before publication.
- Added release readiness checks for package inspection, secret scanning,
  license and notice validation, public hygiene, artifact inventory, serialized
  DCB contract evidence, and whitespace validation.
