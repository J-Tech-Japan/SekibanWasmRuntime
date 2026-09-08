# sekiban-mv

`sekiban-mv` contains the Rust materialized-view WASM boundary contracts for
Sekiban modules. It provides materialized-view DTOs, the `WasmMvProjector` trait,
the `MvParamBuilder`, host-backed query-port support, and the `export_mv!` macro.

Use this crate when a Rust WASM module needs to expose materialized views to the
Sekiban runtime host. The intended public API boundary is the materialized-view
DTO and projector/export surface; host ABI details remain preview implementation
detail until the first public release is approved.

Release status: this crate is a repo-local release candidate for a future
crates.io publication. It is versioned as `0.1.0` and pins its internal
`sekiban-wasm` dependency to the same version while retaining a repository path
for local development. It has not been published to crates.io. Samples should
continue using repository path dependencies until publication is approved.

License: Elastic License 2.0, matching the repository root license.
