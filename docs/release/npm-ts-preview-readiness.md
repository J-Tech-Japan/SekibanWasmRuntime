# npm TypeScript SDK Preview Readiness (SWR-G057)

This document records the package boundaries, metadata decisions, and
compatibility statement for the TypeScript SDK surface. `@sekiban/dcb-core`,
`@sekiban/dcb-domain`, and `@sekiban/dcb-client` **0.2.0** are published public
npm packages (upstream `sekiban-dcb-ts`; not built or released from this
repository). `@sekiban/as-wasm` **0.1.0** remains a repository-owned,
lane-ready/unpublished package released through the `ts-v*` lane (SWR-G058).
This document is the TypeScript counterpart of `rust-crate-preview-readiness.md`.

## Package Boundaries

| Package | Ownership | Contents |
| --- | --- | --- |
| `@sekiban/dcb-core`, `@sekiban/dcb-domain`, `@sekiban/dcb-client` | **Published public npm** (`0.2.0`) | Matched DCB TypeScript SDK: `createSekibanExecutor`, domain command/event helpers, and the typed executor contract. Consumed from the registry by samples and CI in this repo. |
| `@sekiban/as-wasm` | **Repository-owned** (`src/lib/sekiban-as-wasm`) | AssemblyScript projector SDK, shipped as `assembly/` sources (the consumer's `asc` build compiles them together with the projector's own code): pinned-buffer memory management (`alloc`/`dealloc`), string marshalling (`readStr`/`writeStr`, `(ptr << 32 | byteLength)` convention), the `WasmMv*` materialized-view SQL statement protocol DTOs and helpers, and `applyPaging`. |

Boundary rule applied during the extraction: **only domain-agnostic runtime
plumbing moved to `@sekiban/as-wasm`**. Everything that mentions a concrete
domain stayed in the `ts-wasm` sample
(`src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/ts-wasm`):

- SDK-side: memory management, string marshalling, MV protocol DTOs
  (`WasmMvParam`, `WasmMvSqlStatementDto`, `WasmMvTableBindingsDto`,
  `WasmMvMetadataDto`, `WasmMvSerializableEventDto`, `WasmMvStatementBatchDto`,
  `WasmMvErrorDto`), statement/param builders (`statement`, `guidParam`,
  `stringParam`, `int32Param`, `statementBatchPayload`, `errorPayload`,
  `tableName`, `buildIndexName`), and `applyPaging`.
- Sample-side: all weather/student/classroom/meeting-room state and event
  classes, event application, query implementations, projector-name/kind
  registry, and the `ClassRoomEnrollment` view definition with its SQL
  (`mv_metadata`/`mv_initialize`/`mv_apply_event` exports and the logical
  table set are view-specific and therefore projector-author code).

This boundary is judged sufficient for external projector authors: a projector
module needs to re-export `alloc`/`dealloc`, implement the
`create_instance`/`apply_event`/`serialize_state`/`restore_state`/
`execute_query`/`execute_list_query`/`get_event_types` exports for its own
domain, and (optionally) build MV statement batches with the SDK helpers — none
of which requires copying runtime plumbing anymore. The `ts-wasm` sample is the
reference implementation of that shape.

## Metadata Decisions

Both packages carry the same metadata bar, aligned with the Rust crate
metadata policy (`rust-crate-metadata-policy.md`):

| Field | Value |
| --- | --- |
| `version` | `0.1.0` (all SDK languages start at 0.1.0 per the 2026-07-02 grill decisions) |
| `license` | `Elastic-2.0` (SPDX), LICENSE file included in each tarball |
| `author` | `J-Tech Japan, Inc.` |
| `homepage` | `https://github.com/J-Tech-Japan/SekibanWasmRuntime` |
| `repository` | git URL plus `directory` pointing at the package path |
| `keywords` | `sekiban`, `dcb`, `event-sourcing`, `wasm`, plus `cqrs` (`@sekiban/dcb-client`) / `assemblyscript` (`@sekiban/as-wasm`) |
| `files` | Whitelist: `dist` for `@sekiban/dcb-client`, `assembly` for `@sekiban/as-wasm` (README/LICENSE/package.json are always included by npm) |

Package-specific decisions:

- `@sekiban/dcb-client` targets Node.js 20+ (`engines`), is ESM-only (`type: module`)
  with `exports`/`types` mappings, and has zero runtime dependencies (it uses
  the built-in `fetch`). A `prepack` hook rebuilds `dist/` so the tarball can
  never ship stale output.
- `@sekiban/as-wasm` ships TypeScript(-dialect) sources under `assembly/` and
  declares `assemblyscript` (^0.27) and `json-as` (^0.9) as peer dependencies;
  its `ascMain`/`main` point at `assembly/index.ts` so
  `import ... from "@sekiban/as-wasm/assembly"` resolves in consumer `asc`
  builds. Its own `build` script is a compile check
  (`asc assembly/index.ts` into an untracked `build/` directory).
- asc/tsc packaging constraint recorded for future slices: `json-as@0.9`'s
  compiler transform imports `visitor-as`, whose declared peer range stops at
  `assemblyscript@^0.25`. Consumers (and this repo's sample) resolve that with
  an npm `overrides` entry pinning `visitor-as`'s `assemblyscript` peer to the
  project's own version; without it `npm install` fails with `ERESOLVE`.

## Sample Rewiring

`ts-wasm` may consume `@sekiban/as-wasm` through a repo-internal
`file:../../../lib/sekiban-as-wasm` reference inside the monorepo.
`ts-clientapi` and the npm decider sample Client consume the published
`@sekiban/dcb-core/domain/client@0.2.0` matched set from the public npm
registry. The extraction smoke packs only `@sekiban/as-wasm` from this repo;
the DCB client proof uses registry installs.

## Extraction Smoke

`scripts/release/npm-extraction-smoke.sh` (credential-free, publishes nothing):

1. `npm pack` `@sekiban/as-wasm` and validate tarball contents (`assembly/` +
   README + LICENSE + package.json only).
2. Compile the `ts-wasm` projector sources against the packed
   `@sekiban/as-wasm` tarball in an isolated consumer directory, with a
   no-local-path guard asserting the lockfile resolved `@sekiban/as-wasm` to
   the tarball (never `src/lib`), and verify the module's required exports.
3. Clean-install and test the migrated small npm Client against registry
   `@sekiban/dcb-core/domain/client@0.2.0`.
4. Load the produced `.wasm` in the public runtime container
   (`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`, default tag
   `1.0.0-preview.3`, overridable via `SAMPLE_RUNTIME_IMAGE_TAG`) with a
   disposable Postgres sidecar, wait for the strict `/ready` check, then use
   the registry-installed DCB client to commit a `WeatherForecastCreated`
   event and read it back through tag-state and list/count queries.

If Docker is unavailable, step 4 is reported as an explicit `SKIPPED`
container step in the report (`artifacts/release/npm-extraction-smoke.md`);
steps 1–3 must always pass.

Verified locally on 2026-07-02: full PASS including the container step —
`/ready` 200, committed event visible in tag-state (version 1) and in the list
query result.

Note: the preview.3 image's sqlite storage provider crashes at startup (its
relational migration path resolves the Postgres `DbContext` factory), which is
why the smoke uses a Postgres sidecar like the compose sample rather than
sqlite.

## Compatibility Statement

`@sekiban/dcb-client` 0.2.0 and `@sekiban/as-wasm` 0.1.0 are compatible with:

- **Runtime image** `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`
  — proven by the extraction smoke above (module load, command commit,
  tag-state, list query).
- **Rust 0.1.0 crates** — `@sekiban/dcb-client` speaks the same serialized HTTP
  contract (`/api/sekiban/serialized/tag-state|commit|query|list-query`) as
  `sekiban-executor` 0.1.0, and `@sekiban/as-wasm` implements the same guest
  ABI (alloc/dealloc string marshalling and the `WasmMv*` materialized-view
  statement protocol) as `sekiban-wasm`/`sekiban-mv` 0.1.0. Modules built with
  either SDK run side by side on the same runtime image.

## SWR-G059 npm Consumer Sample Against the Public GHCR Runtime

SWR-G059 added `src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider`, the npm
counterpart of the crates.io Rust sample (SWR-G056): an external-consumer
proof that depends on `@sekiban/as-wasm` at exact npm `0.1.0` and `@sekiban/dcb-client` at exact npm `0.2.0`
versions only (`Wasm/package.json`, `Client/package.json`; no `file:`/
`link:`/relative-path references, guarded by
`scripts/verify-no-local-sekiban-paths.sh`), with a sample-owned Aspire
AppHost provisioning Postgres and the public GHCR runtime container.

Because neither package is published yet, the guard is static (no live
`npm install` against the registry, unlike the Rust guard's `cargo check`
against already-published crates.io crates). Both `scripts/build-wasm.sh` and
`scripts/smoke.sh` accept `SEKIBAN_NPM_MODE=tarball|registry`:

- `tarball` packs `@sekiban/as-wasm`/`@sekiban/dcb-client` from `src/lib` with
  `npm pack` and installs each from its packed tarball in a scratch build
  directory (never rewriting the committed `package.json`), with a guard
  asserting the installed package resolved from the `.tgz`. This mode passes
  today.
- `registry` (the default, and the mode that becomes real after publish)
  runs a plain `npm install`; today this 404s, and both scripts report
  `SKIP` rather than `FAIL`.

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/scripts/verify-no-local-sekiban-paths.sh
env SEKIBAN_NPM_MODE=tarball bash src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/scripts/build-wasm.sh
env -u SAMPLE_RUNTIME_IMAGE_TAG SEKIBAN_NPM_MODE=tarball bash src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/scripts/smoke.sh
```

Verified locally on 2026-07-03 in tarball mode: full PASS against
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` --
`CreateWeatherForecast` + `UpdateWeatherForecastLocation` committed through
`createSekibanExecutor`, tag-state read back (version 2, location `Osaka`),
`GetWeatherForecastListQuery`/`GetWeatherForecastCountQuery` both returned the
forecast, and the `WeatherForecast` materialized view caught up in
`DcbMaterializedViewPostgres` (`sekiban_mv_weatherforecast_v1_weather_forecast`).
Report: `reports/smoke/npm-ts-decider-smoke.md`.

**API gap found**: `createSekibanExecutor.executeQuery`/`executeListQuery`
(`@sekiban/dcb-client`) have no host-side wait-for-sortable-id parameter, unlike the
Go SDK's `ExecuteListQuery(queryType, paramsJson, waitForSortableUniqueId)`.
The sample's query params carry a `waitForSortableUniqueId` field that the
WASM module can read, but nothing on the host blocks on it before invoking
the module, so the sample's client polls client-side for catch-up (mirroring
the Rust smoke client's own retry loop) rather than relying on a blocking
host wait. This is a candidate follow-up for a future `@sekiban/dcb-client` release,
not addressed in this slice (kept out of scope per the SWR-G059 packet).

**Pending evidence**: the registry-mode run
(`SEKIBAN_NPM_MODE=registry`, `dotnet`/`npm install` without a tarball
override) is still outstanding and tracked here until the `ts-v*` publish
batch (SWR-G058) completes; re-run the same three commands with
`SEKIBAN_NPM_MODE=registry` (or omit it, since that is the default) once
`@sekiban/dcb-client` 0.2.0 and `@sekiban/as-wasm` 0.1.0 are live on npm, and update this
section with the result.

## Out of Scope (deferred)

- npm publish, tokens, or trusted publishing (human-gated batch).
- The `ts-v*` release workflow and `npm-release` protected environment
  (SWR-G058).
- The registry-mode confirmation of the SWR-G059 npm consumer sample (see
  above; pending until the npm publish batch completes).
- `@sekiban/aspire`, `create-sekiban-wasm`, and any `@sekiban/dcb-client` API split.
