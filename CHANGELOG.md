# Changelog

All notable public-preview changes for SekibanWasmRuntime are tracked here.

This repository is currently on the `1.0.0-preview.*` version line. See
[`docs/release/versioning-and-changelog.md`](docs/release/versioning-and-changelog.md)
for the version, changelog, migration-note, and compatibility evidence rules.

## Unreleased

- Added a NuGet preview GitHub Release checklist and release notes template for
  release operators.
- Documented the preview version, changelog, migration-note, and compatibility
  evidence source-of-truth for release operators.
- Clarified that breaking public contract changes require migration guidance,
  compatibility evidence, and GitHub Release notes before publication.

## 1.0.0-preview.1

- Established the initial public preview package baseline for:
  `Sekiban.Dcb.WasmRuntime`, `Sekiban.Dcb.WasmRuntime.Remote`, and
  `Sekiban.Dcb.WasmRuntime.Wasmtime`.
- Added release readiness checks for package inspection, secret scanning,
  license and notice validation, public hygiene, artifact inventory, serialized
  DCB contract evidence, and whitespace validation.
