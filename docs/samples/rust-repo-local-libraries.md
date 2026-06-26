# Rust Repo-Local Library Samples

Rust samples in this repository currently use path dependencies to the shared
Rust crates under `src/wasm-projectors/rust`:

- `sekiban-core`
- `sekiban-derive`
- `sekiban-executor`
- `sekiban-wasm`
- `sekiban-mv`

Those crates are public-release candidates, but they have not been published to
crates.io. Treat Rust sample `Cargo.toml` path dependencies as intentional until
the release-prep work adds package metadata, versioned inter-crate dependencies,
and an external-consumer smoke that uses crates.io dependencies.

The readiness inventory is maintained in
[`../release/rust-crate-preview-readiness.md`](../release/rust-crate-preview-readiness.md).
