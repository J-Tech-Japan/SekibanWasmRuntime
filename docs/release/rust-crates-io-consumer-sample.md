# Rust crates.io Consumer Sample

SWR-G055 adds
`src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider` as the first Rust sample
that consumes the published Sekiban crates from crates.io instead of repository
paths.

## Dependency Boundary

The sample uses exact `=0.1.0` dependencies for public Sekiban crates:

- `sekiban-core`
- `sekiban-derive`
- `sekiban-wasm`
- `sekiban-mv`
- `sekiban-executor`

It deliberately does not depend on `sekiban-wasm-domain`, because that crate is
an unpublished repository helper. The sample owns its weather domain source
directly so it matches what an external Rust consumer would write.

## Verification

Run from the repository root:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/scripts/verify-no-local-sekiban-paths.sh
```

The script fails when any new sample manifest reintroduces a local
`src/wasm-projectors/rust` path dependency or the unpublished
`sekiban-wasm-domain` crate. It also runs:

```bash
cargo metadata --manifest-path src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/Cargo.toml --format-version 1
cargo check --manifest-path src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/Cargo.toml --workspace
```

Build the WASM package and generated runtime manifest with:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/scripts/build-wasm.sh
```

## Closeout Note

The published crates are sufficient for a standalone sample-owned domain, WASM
projection module, materialized-view boundary, and typed HTTP client. The sample
still needs a running runtime host for end-to-end execution, but the compile and
metadata checks prove the Rust package boundary no longer depends on this
repository's local Rust crate paths.

