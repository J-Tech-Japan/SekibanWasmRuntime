# crates.io Rust Decider Sample

This sample proves the published Rust crates can be consumed like an external
application. It intentionally avoids repository-local dependencies on
`src/wasm-projectors/rust/sekiban-*` and does not use the unpublished
`sekiban-wasm-domain` helper crate.

The sample is split into three crates:

- `Domain`: sample-owned command/event/tag/projector/materialized-view code.
- `Wasm`: exports the domain and materialized-view boundary.
- `Client`: uses `RemoteSekibanExecutor` against a running public runtime host.

Sekiban package dependencies are exact crates.io requirements:

```toml
sekiban-core = "=0.1.0"
sekiban-derive = "=0.1.0"
sekiban-wasm = "=0.1.0"
sekiban-mv = "=0.1.0"
sekiban-executor = "=0.1.0"
```

Run the dependency guard and compile check from the repository root:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/scripts/verify-no-local-sekiban-paths.sh
```

The guard runs `cargo metadata`, `cargo check --workspace`, and fails if any new
sample manifest references `path = ...wasm-projectors/rust` or
`sekiban-wasm-domain`.

Build the WASM module and generated runtime manifest:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/scripts/build-wasm.sh
```

Generated artifacts are staged under
`artifacts/samples/crates-io-rs-decider/{modules,config}` and are not checked in.
With a runtime host already running against that module and manifest, run the
typed client:

```bash
RUNTIME_URL=http://localhost:8080 \
  cargo run --manifest-path src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/Cargo.toml \
    --package crates-io-rs-decider-client
```

The client creates a forecast, updates its location, reads tag state, executes a
list query, executes a count query, and prints JSON smoke evidence.

## End-to-end smoke against the public GHCR runtime

`scripts/smoke.sh` runs the full public-artifact end-to-end path: it builds the
WASM module (if needed), starts an Aspire AppHost that provisions Postgres and
the **public GHCR runtime container**
(`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`, default `1.0.0-preview.3`,
override with `SAMPLE_RUNTIME_IMAGE_TAG`), runs the typed Rust client, and then
confirms the materialized view caught up in `DcbMaterializedViewPostgres`.

```bash
env -u SAMPLE_RUNTIME_IMAGE_TAG \
  bash src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/scripts/smoke.sh
```

The smoke validates, end to end, using only crates.io `=0.1.0` Sekiban
dependencies and the public runtime image:

- command execution (`CreateWeatherForecast` + `UpdateWeatherForecastLocation`),
- tag-state readback,
- in-memory projection queries (`GetWeatherForecastListQuery`,
  `GetWeatherForecastCountQuery`),
- materialized-view catch-up/read in `DcbMaterializedViewPostgres`.

It writes a report to `reports/smoke/crates-io-rs-decider-smoke.md` and skips
gracefully (exit 0, `Result: SKIP`) when Docker, the .NET SDK, cargo, or the
`wasm32-wasip1` target are unavailable.

### How this differs from `PublicContainer.RsDecider`

Both samples drive the same public GHCR runtime container, but they prove
different boundaries:

- This sample (`CratesIo.RsDecider`) consumes the **published crates.io** Sekiban
  crates at exact `=0.1.0` and owns its domain code, so it is an external
  public-package consumer proof. The `verify-no-local-sekiban-paths.sh` guard
  fails if a local `src/wasm-projectors/rust/sekiban-*` path or the unpublished
  `sekiban-wasm-domain` helper is reintroduced, and also asserts the AppHost
  targets the public GHCR image.
- `PublicContainer.RsDecider` proves the public runtime container can execute
  Rust command/query/MV behavior, but it still builds from repository-local Rust
  path dependencies, so it is not an external public-package consumer proof.
