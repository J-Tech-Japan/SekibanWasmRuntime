# npm TypeScript Decider Sample

This sample proves the published TypeScript packages can be consumed like an
external application. It intentionally avoids repository-local dependencies
on `src/lib/sekiban-ts` / `src/lib/sekiban-as-wasm` (no `file:`/`link:`/
relative-path references) and mirrors the shape of the crates.io Rust sample
(`src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider`, SWR-G056).

The sample is split into three parts:

- `Wasm`: an AssemblyScript projector built on `@sekiban/as-wasm`, exporting
  the weather-forecast domain and materialized-view boundary.
- `Client`: a typed `SekibanRuntimeClient` (`@sekiban/ts`) smoke client
  against a running public runtime host.
- `AppHost`: a sample-owned Aspire AppHost that provisions Postgres and the
  **public GHCR runtime container**.

Sekiban package dependencies are exact npm requirements:

```json
"@sekiban/as-wasm": "0.1.0"   // Wasm/package.json
"@sekiban/ts": "0.1.0"        // Client/package.json
```

## Two consumption modes

`@sekiban/ts` and `@sekiban/as-wasm` are not published to npm yet (the
`ts-v*` release lane, SWR-G058, is credential-free but has not run a real
publish). `scripts/build-wasm.sh` and `scripts/smoke.sh` select how the
packages are resolved via `SEKIBAN_NPM_MODE`:

- `tarball` (works today): packs `@sekiban/as-wasm` and `@sekiban/ts` from
  `src/lib/sekiban-as-wasm` / `src/lib/sekiban-ts` with `npm pack`, and
  installs each from its packed tarball in a scratch build directory. The
  committed `package.json` files are never rewritten; the tarball path is
  substituted only in the scratch copy, with a guard asserting the installed
  package actually resolved from the `.tgz` (never `src/lib`).
- `registry` (default, becomes the real path after publish): a plain
  `npm install` against the npm registry. This fails today with a 404 for
  `@sekiban/as-wasm@0.1.0` / `@sekiban/ts@0.1.0` -- that failure is expected
  and both scripts report it as `SKIP` rather than `FAIL`. The registry-mode
  run becomes the recorded follow-up once SWR-G058 publishes.

Run the dependency guard (static; requires no registry access):

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/scripts/verify-no-local-sekiban-paths.sh
```

Build the WASM module and generated runtime manifest in tarball mode:

```bash
env SEKIBAN_NPM_MODE=tarball \
  bash src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/scripts/build-wasm.sh
```

Generated artifacts are staged under
`artifacts/samples/npm-ts-decider/{modules,config}` and are not checked in.

## End-to-end smoke against the public GHCR runtime

`scripts/smoke.sh` runs the full public-artifact end-to-end path: it builds
the WASM module (if needed), starts an Aspire AppHost that provisions
Postgres and the **public GHCR runtime container**
(`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`, default `1.0.0-preview.3`,
override with `SAMPLE_RUNTIME_IMAGE_TAG`), runs the typed TypeScript client,
and then confirms the materialized view caught up in
`DcbMaterializedViewPostgres`.

```bash
env -u SAMPLE_RUNTIME_IMAGE_TAG SEKIBAN_NPM_MODE=tarball \
  bash src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/scripts/smoke.sh
```

The smoke validates, end to end, using only npm `0.1.0` Sekiban dependencies
and the public runtime image:

- command execution (`CreateWeatherForecast` + `UpdateWeatherForecastLocation`),
- tag-state readback,
- in-memory projection queries (`GetWeatherForecastListQuery`,
  `GetWeatherForecastCountQuery`),
- materialized-view catch-up/read in `DcbMaterializedViewPostgres`.

It writes a report to `reports/smoke/npm-ts-decider-smoke.md` and skips
gracefully (exit 0, `Result: SKIP`) when Docker, the .NET SDK, npm, or node
are unavailable, or when registry mode cannot resolve the not-yet-published
packages.

## API gap found while writing this sample

`SekibanRuntimeClient.executeQuery`/`executeListQuery` (`@sekiban/ts`) have
no host-side wait-for-sortable-id parameter, unlike the Go SDK's
`ExecuteListQuery(queryType, paramsJson, waitForSortableUniqueId)`. The
`waitForSortableUniqueId` field this sample's `GetWeatherForecastListQuery`
params accept is informational only (read by the WASM module, not enforced
by the host before it is called), so this client polls for catch-up itself
(mirroring the Rust smoke client's own retry loop) rather than relying on a
blocking host wait. See `docs/release/npm-ts-preview-readiness.md` for the
tracked follow-up.

### How this differs from `Sekiban.Dcb.Orleans.Decider.Wasm.Ts`

Both samples use `@sekiban/ts` and `@sekiban/as-wasm`, but they prove
different boundaries:

- This sample (`Npm.TsDecider`) consumes the packages at exact npm `0.1.0`
  registry-style versions with no local path dependencies, and drives the
  **public GHCR runtime container** through a sample-owned Aspire AppHost --
  an external public-package consumer proof.
- `Sekiban.Dcb.Orleans.Decider.Wasm.Ts` consumes the packages through
  repository-local `file:../../../lib/...` references (acceptable inside the
  repo) and self-hosts a full in-process Orleans+Wasmtime runtime; it is the
  broader reference implementation the SDK boundary was extracted from
  (SWR-G057), not an external-consumer proof.
