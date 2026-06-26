# Public Container Rust Decider Sample

The [`Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider`](../../src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider)
sample is the Rust public-container local-development smoke. It runs the
published GHCR runtime host image with a Rust-built Decider WASM module and
Postgres, without using the repository-local runtime host project.

What it demonstrates:

- A Rust weather Decider domain and WASM module built from repo-local crates
  under `src/wasm-projectors/rust`.
- An Aspire AppHost that uses `AddContainer` with
  `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` by default,
  plus Postgres databases for `SekibanDcb` and `DcbMaterializedViewPostgres`.
- Stable staging of `public-container-rs-decider.wasm` and
  `sekiban-manifest.json` under
  `artifacts/samples/public-container-rs-decider/{modules,config}`.
- A typed Rust smoke client using `RemoteSekibanExecutor`, `Command`, `Query`,
  and `ListQuery` runtime abstractions.
- End-to-end command commit, tag-state/memory projection, serialized list-query,
  and caller-owned materialized-view read evidence.

Run it:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider/scripts/build-wasm.sh
env -u SAMPLE_RUNTIME_IMAGE_TAG bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider/scripts/smoke.sh
```

The Rust package publication lane is intentionally deferred. Until these crates
are published to crates.io, this sample uses repository-local Rust path
dependencies for the domain, WASM export, materialized-view boundary, and remote
executor crates. The Rust crates now have release-prep metadata and exact
versioned internal path dependencies, but this sample remains repo-local until a
later publication-gate packet proves crates.io consumption.
