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

