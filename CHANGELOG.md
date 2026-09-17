# Changelog

All notable public-preview changes for SekibanWasmRuntime are tracked here.

This repository is currently on the `1.0.0-preview.*` version line. See
[`docs/release/versioning-and-changelog.md`](docs/release/versioning-and-changelog.md)
for the version, changelog, migration-note, and compatibility evidence rules.

## Unreleased

_No unreleased public-preview changes beyond the sections below._

## 1.0.0-preview.7

- **SWR-G089 / PR #296:** Retire the unpublished `@sekiban/ts` package. TypeScript
  samples, release smoke, generator metadata, and CI now consume the published
  `@sekiban/dcb-core`, `@sekiban/dcb-domain`, and `@sekiban/dcb-client` `0.2.0`
  packages with `createSekibanExecutor` and registry-backed transport tests.
- **SWR-G092 / PR #298:** Extend the component WIT `apply-event` export with
  `event-tags`, pass the ordered tag list through
  `WasmtimeComponentProjectionInstance`, and forward it unchanged in the
  TypeScript guest. Advance the meeting-room domain pin to `681da42`
  (`@sekiban/dcb-domain@0.2.0`) with `TagProbeProjector` tag-fidelity evidence.
- **DCB baseline 10.22.0:** Update every centrally managed `Sekiban.Dcb.*`
  package dependency, align `submodules/Sekiban` to tag `dcb-v10.22.0`, and
  raise `Microsoft.Orleans.*` to `10.3.1` plus `Microsoft.Extensions.*` to
  `10.0.5` for the DCB 10.22 transitive graph. The local serialized-commit raw
  gate (SWR-G087 / F-007) remains fail-closed on explicit-`null` collection
  members despite the upstream 10.20.0+ presence gate.
- **GHCR runtime-host catch-up:** Move release-lane defaults and consumer docs
  to `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.7` so the
  image lane matches the NuGet preview.7 package line after operator publish.

## 1.0.0-preview.1

- Release status: GitHub Release notes, changelog content, dry-run evidence,
  compatibility evidence, and publish-gate documentation are prepared for the
  NuGet preview release. The actual GitHub Release and NuGet publish remain
  blocked until the `nuget-preview` protected environment and NuGet.org Trusted
  Publishing policy are confirmed in repository/package-owner settings.
- Established the initial public preview package baseline for:
  `Sekiban.Dcb.WasmRuntime`, `Sekiban.Dcb.WasmRuntime.Remote`, and
  `Sekiban.Dcb.WasmRuntime.Wasmtime`.
- Hardened the NuGet preview release workflow so readiness runs the serialized
  DCB contract baseline and real release publishes use NuGet.org Trusted
  Publishing instead of a long-lived NuGet API key.
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
