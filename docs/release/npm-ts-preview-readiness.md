# npm TypeScript SDK Preview Readiness (SWR-G057)

This document records the package boundaries, metadata decisions, and
compatibility statement for the first publishable versions of the TypeScript
SDK surface: `@sekiban/ts` 0.1.0 and `@sekiban/as-wasm` 0.1.0. It is the
TypeScript counterpart of `rust-crate-preview-readiness.md`.

No npm publish happened in this slice. Publishing is a separate, human-gated
batch (the `ts-v*` release lane is SWR-G058); both package names were verified
unclaimed under the active, owned `@sekiban` scope on 2026-07-02.

## Package Boundaries

| Package | Path | Contents |
| --- | --- | --- |
| `@sekiban/ts` | `src/lib/sekiban-ts` | Thin Node.js host SDK: `SekibanRuntimeClient` (tag-state, serialized query/list-query, command commit), the typed `Command`/`CommandContext`/`CommandOutput` contract, and command helpers/errors. Compiled with `tsc` to `dist/`. |
| `@sekiban/as-wasm` | `src/lib/sekiban-as-wasm` | AssemblyScript projector SDK, shipped as `assembly/` sources (the consumer's `asc` build compiles them together with the projector's own code): pinned-buffer memory management (`alloc`/`dealloc`), string marshalling (`readStr`/`writeStr`, `(ptr << 32 | byteLength)` convention), the `WasmMv*` materialized-view SQL statement protocol DTOs and helpers, and `applyPaging`. |

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
| `keywords` | `sekiban`, `dcb`, `event-sourcing`, `wasm`, plus `cqrs` (`@sekiban/ts`) / `assemblyscript` (`@sekiban/as-wasm`) |
| `files` | Whitelist: `dist` for `@sekiban/ts`, `assembly` for `@sekiban/as-wasm` (README/LICENSE/package.json are always included by npm) |

Package-specific decisions:

- `@sekiban/ts` targets Node.js 20+ (`engines`), is ESM-only (`type: module`)
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

`ts-wasm` now consumes `@sekiban/as-wasm` through a repo-internal
`file:../../../lib/sekiban-as-wasm` reference (acceptable inside the repo per
the issue contract) and `npm run build` still emits
`src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Ts/modules/ts-weather.wasm` with
an unchanged export surface. `ts-clientapi` keeps its existing
`file:../../../lib/sekiban-ts` reference. The extraction smoke never uses these
`file:` references — it proves both packages from packed tarballs.

## Extraction Smoke

`scripts/release/npm-extraction-smoke.sh` (credential-free, publishes nothing):

1. `npm pack` both packages and validate tarball contents (`@sekiban/ts`:
   `dist/` + README + LICENSE + package.json only; `@sekiban/as-wasm`:
   `assembly/` + README + LICENSE + package.json only).
2. Compile the `ts-wasm` projector sources against the packed
   `@sekiban/as-wasm` tarball in an isolated consumer directory, with a
   no-local-path guard asserting the lockfile resolved `@sekiban/as-wasm` to
   the tarball (never `src/lib`), and verify the module's required exports.
3. Compile the `ts-clientapi` sources against the packed `@sekiban/ts`
   tarball, with the same lockfile guard.
4. Load the produced `.wasm` in the public runtime container
   (`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`, default tag
   `1.0.0-preview.3`, overridable via `SAMPLE_RUNTIME_IMAGE_TAG`) with a
   disposable Postgres sidecar, wait for the strict `/ready` check, then use
   the packed `@sekiban/ts` client to commit a `WeatherForecastCreated` event
   and read it back through tag-state and `GetWeatherForecastListQuery`.

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

`@sekiban/ts` 0.1.0 and `@sekiban/as-wasm` 0.1.0 are compatible with:

- **Runtime image** `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`
  — proven by the extraction smoke above (module load, command commit,
  tag-state, list query).
- **Rust 0.1.0 crates** — `@sekiban/ts` speaks the same serialized HTTP
  contract (`/api/sekiban/serialized/tag-state|commit|query|list-query`) as
  `sekiban-executor` 0.1.0, and `@sekiban/as-wasm` implements the same guest
  ABI (alloc/dealloc string marshalling and the `WasmMv*` materialized-view
  statement protocol) as `sekiban-wasm`/`sekiban-mv` 0.1.0. Modules built with
  either SDK run side by side on the same runtime image.

## Out of Scope (deferred)

- npm publish, tokens, or trusted publishing (human-gated batch).
- The `ts-v*` release workflow and `npm-release` protected environment
  (SWR-G058).
- The npm registry-consumer sample and public-container E2E proof (SWR-G059).
- `@sekiban/aspire`, `create-sekiban-wasm`, and any `@sekiban/ts` API split.
