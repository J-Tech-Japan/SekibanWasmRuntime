# Public Container Rust Decider Sample

This sample proves the Rust Decider local-development path against the public
runtime container image:

- Rust Decider domain and WASM package are built from repository-local Rust
  crates under `src/wasm-projectors/rust`.
- The Aspire AppHost runs
  `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` by default
  with `AddContainer`, Postgres, and read-only mounts for the generated module
  and manifest.
- The smoke uses the Rust `RemoteSekibanExecutor`, `Command`, and `ListQuery`
  abstractions to commit and read a weather forecast, then checks the
  `WeatherForecast` materialized view directly in `DcbMaterializedViewPostgres`.

Run it from the repository root:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider/scripts/build-wasm.sh
env -u SAMPLE_RUNTIME_IMAGE_TAG bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider/scripts/smoke.sh
```

Generated artifacts are staged under
`artifacts/samples/public-container-rs-decider/{modules,config}` and are not
checked in.
