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
the publication-gate work publishes the internal crate train and adds an
external-consumer smoke that uses crates.io dependencies.

The crates now include release-prep metadata, crate-local READMEs, crate-level
preview API docs, and exact versioned internal path dependencies. Dependent
crate package dry-runs still stop at the expected pre-publication blocker because
Cargo resolves the packaged dependency from crates.io after stripping local path
entries, and the upstream internal crates are not published yet.

The readiness inventory is maintained in
[`../release/rust-crate-preview-readiness.md`](../release/rust-crate-preview-readiness.md).
