# npm TypeScript Decider Sample

This sample proves the published TypeScript packages can be consumed like an
external application. It intentionally avoids repository-local dependencies
(no `file:`/`link:`/relative-path references to `src/lib`) and mirrors the
shape of the crates.io Rust sample
(`src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider`, SWR-G056).

The sample is split into three parts:

- `Wasm`: an AssemblyScript projector built on `@sekiban/as-wasm`, exporting
  the weather-forecast domain and materialized-view boundary.
- `Client`: a typed `createSekibanExecutor` client using published
  `@sekiban/dcb-core`, `@sekiban/dcb-domain`, and `@sekiban/dcb-client`.
- `AppHost`: a sample-owned Aspire AppHost that provisions Postgres and the
  **public GHCR runtime container**.

Sekiban package dependencies are exact npm requirements:

```json
"@sekiban/as-wasm": "0.1.0"              // Wasm/package.json — lane-ready, unpublished in this repo
"@sekiban/dcb-client": "0.2.0"           // Client/package.json — published on npm
"@sekiban/dcb-core": "0.2.0"             // transitive via dcb-client
"@sekiban/dcb-domain": "0.2.0"           // transitive via dcb-client
```

`@sekiban/dcb-core`, `@sekiban/dcb-domain`, and `@sekiban/dcb-client` are
**published public npm packages**. Only `@sekiban/as-wasm` (and `@sekiban/aspire`
elsewhere in this repo) remain lane-ready/unpublished repository-owned packages.

## Two consumption modes for `@sekiban/as-wasm`

`scripts/build-wasm.sh` and `scripts/smoke.sh` select how the Wasm projector
SDK is resolved via `SEKIBAN_NPM_MODE`:

- `tarball`: packs `@sekiban/as-wasm` from `src/lib/sekiban-as-wasm` with
  `npm pack` and installs from the tarball in a scratch build directory.
  The Client always resolves `@sekiban/dcb-*` from the public npm registry.
- `registry` (default): plain `npm install` for all packages. The DCB trio
  resolves from npm today. `@sekiban/as-wasm@0.1.0` may still 404 until the
  `ts-v*` lane publishes it; scripts report that as `SKIP` rather than `FAIL`.

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
(`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`, default `1.0.0-preview.7`,
override with `SAMPLE_RUNTIME_IMAGE_TAG`), runs the typed TypeScript client,
and then confirms the materialized view caught up in
`DcbMaterializedViewPostgres`.

```bash
env -u SAMPLE_RUNTIME_IMAGE_TAG SEKIBAN_NPM_MODE=tarball \
  bash src/samples/Sekiban.Dcb.WasmRuntime.Npm.TsDecider/scripts/smoke.sh
```

The smoke validates, end to end, using registry `@sekiban/dcb-*@0.2.0`,
tarball or registry `@sekiban/as-wasm@0.1.0`, and the public runtime image:

- command execution (`CreateWeatherForecast` + `UpdateWeatherForecastLocation`),
- tag-state readback,
- in-memory projection queries (`GetWeatherForecastListQuery`,
  `GetWeatherForecastCountQuery`),
- materialized-view catch-up/read in `DcbMaterializedViewPostgres`.

It writes a report to `reports/smoke/npm-ts-decider-smoke.md` and skips
gracefully (exit 0, `Result: SKIP`) when Docker, the .NET SDK, npm, or node
are unavailable, or when `@sekiban/as-wasm` cannot be resolved in registry mode.

## API gap found while writing this sample

`createSekibanExecutor.executeQuery`/`executeListQuery` (`@sekiban/dcb-client`) have
no host-side wait-for-sortable-id parameter, unlike the Go SDK's
`ExecuteListQuery(queryType, paramsJson, waitForSortableUniqueId)`. The
`waitForSortableUniqueId` field this sample's `GetWeatherForecastListQuery`
params accept is informational only (read by the WASM module, not enforced
by the host before it is called), so this client polls for catch-up itself
(mirroring the Rust smoke client's own retry loop) rather than relying on a
blocking host wait. See `docs/release/npm-ts-preview-readiness.md` for the
tracked follow-up.

### How this differs from `Sekiban.Dcb.Orleans.Decider.Wasm.Ts`

Both samples use the same published `@sekiban/dcb-*` packages and
`@sekiban/as-wasm`, but they prove different boundaries:

- This sample (`Npm.TsDecider`) consumes DCB packages from the public npm
  registry at exact `0.2.0` pins and drives the **public GHCR runtime
  container** through a sample-owned Aspire AppHost — an external
  public-package consumer proof.
- `Sekiban.Dcb.Orleans.Decider.Wasm.Ts` is the broader in-repo reference
  implementation (Orleans + Wasmtime + ts-clientapi) that also consumes the
  same published DCB packages from npm; it is not an external-consumer-only
  proof like this sample.
