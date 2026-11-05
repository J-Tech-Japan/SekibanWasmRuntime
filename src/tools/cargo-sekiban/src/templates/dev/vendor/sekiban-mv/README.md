# sekiban-mv

`sekiban-mv` contains the Rust materialized-view WASM boundary contracts for
Sekiban modules. It provides materialized-view DTOs, the `WasmMvProjector` trait,
the `MvParamBuilder`, host-backed query-port support, and the `export_mv!` macro.

Use this crate when a Rust WASM module needs to expose materialized views to the
Sekiban runtime host. The intended public API boundary is the materialized-view
DTO and projector/export surface; host ABI details remain preview implementation
detail until the first public release is approved.

Release status: `0.1.0` is published on crates.io but its `export_mv!` metadata
omitted `abiVersion` and `capabilities`, so the runtime host rejects
`mv_metadata` from modules built against that line. Use `0.1.1` or newer for
registry-backed Rust WASM modules. The crate pins `sekiban-wasm = "=0.1.0"` and
retains a repository path for local development.

License: Elastic License 2.0, matching the repository root license.
