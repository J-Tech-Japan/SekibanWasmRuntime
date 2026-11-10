# Changelog

All notable public-preview changes for SekibanWasmRuntime are tracked here.

This repository is currently on the `1.0.0-preview.*` version line. See
[`docs/release/versioning-and-changelog.md`](docs/release/versioning-and-changelog.md)
for the version, changelog, migration-note, and compatibility evidence rules.

## Unreleased

- **SWR-G099:** Centrally managed `Sekiban.Dcb.*` dependencies and the
  `submodules/Sekiban` gitlink move to Sekiban.Dcb `10.24.0` / tag
  `dcb-v10.24.0`; `Microsoft.Orleans.*` moves to `10.4.2`. The DCB public API
  and event-data layout are unchanged, so no migration is required. The SWR
  package line stays `1.0.0-preview.7`.

## 1.0.0-preview.7

- **SWR-G089 / PR #296:** Retire the unpublished TypeScript host SDK package. TypeScript
  samples, release smoke, generator metadata, and CI now consume the published
  `@sekiban/dcb-core`, `@sekiban/dcb-domain`, and `@sekiban/dcb-client` `0.2.0`
  packages with `createSekibanExecutor` and registry-backed transport tests.
- **SWR-G092 / PR #298:** Extend the component WIT `apply-event` export with
  `event-tags`, pass the ordered tag list through
  `WasmtimeComponentProjectionInstance`, and forward it unchanged in the
  TypeScript guest. Advance the meeting-room domain pin to `681da42`
  (`@sekiban/dcb-domain@0.2.0`) with `TagProbeProjector` tag-fidelity evidence.
- **SWR-G093 / PR #301:** Align machine-readable runtime-host HTTP failure
  `code` values with the shared `@sekiban/dcb-client` `FAILURE_KINDS` spelling
  (`timeout`, `unknown_outcome`, `projection_unavailable`).
- **SWR-G095 / PR #305:** Add the explicit
  `Sekiban:MaterializedView:InitializationMode` setting
  (`Sekiban__MaterializedView__InitializationMode`; `VerifyAndExecute` or
  `CreateOrEnsure`). Production stays `VerifyAndExecute`; only the dev AppHost,
  the decider template, and the cargo-sekiban AppHosts opt in to
  `CreateOrEnsure` for schema-less guests. The runtime host does not run MV DDL
  at startup. The configured `IServiceIdProvider` is now re-registered after
  storage registration, so the event store uses the configured ServiceId
  instead of `default`.
- **SWR-G096 / PR #306:** Re-pin TypeScript consumers to
  `@sekiban/dcb-core`, `@sekiban/dcb-domain`, and `@sekiban/dcb-client` `0.3.0`.
  The host does not emit `query_params_unsupported` and has no conditional-commit
  route.
- **SWR-G097 / PR #308:** Bump the e2e Playwright dependency to `1.64.1` so
  chromium installs on Ubuntu 26.04 runners.
- **DCB baseline 10.23.4:** Update every centrally managed `Sekiban.Dcb.*`
  package dependency, align `submodules/Sekiban` to tag `dcb-v10.23.4`, and
  raise `Microsoft.Orleans.*` to `10.3.1` plus `Microsoft.Extensions.*` to
  `10.0.5` for the DCB 10.23 transitive graph. No data or schema migration is
  required for 10.23.x. The local serialized-commit raw
  gate (SWR-G087 / F-007) remains fail-closed on explicit-`null` collection
  members despite the upstream 10.20.0+ presence gate.
- **GHCR runtime-host catch-up:** Move release-lane defaults and consumer docs
  to `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.7` so the
  image lane matches the NuGet preview.7 package line after operator publish.

### Migration notes for 1.0.0-preview.7

- **ServiceId:** events are now written and read under the configured ServiceId
  (`SEKIBAN_SERVICE_ID`; the Aspire helper defaults to `sekiban-wasm-local`).
  Rows written by earlier previews under ServiceId `default` are not visible
  after upgrading. The host refuses `default` as its ServiceId at startup, so
  there is no compatibility switch: start from a fresh store, or re-key the
  existing rows to the configured ServiceId out of band before upgrading.
- **Materialized views:** a guest that does not return a schema contract needs
  `Sekiban__MaterializedView__InitializationMode=CreateOrEnsure` in local
  development. Production deployments keep the default `VerifyAndExecute` and
  provision MV tables and registry rows out of band.

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
