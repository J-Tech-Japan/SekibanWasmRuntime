# sekiban-wasm

`sekiban-wasm` contains WASM export boundary helpers for Rust projection modules
loaded by the Sekiban runtime host. It provides export macros, ABI DTOs,
manifest helpers, memory helpers, and compatibility types used by Rust crates
compiled to `wasm32-wasip2`.

Use this crate when building a Rust domain module that exports Sekiban projection
behavior to the WASM runtime host. The intended public API boundary is the macro
and ABI helper surface needed by domain crates; low-level memory and FFI details
remain preview implementation detail.

Release status: this crate is a repo-local release candidate for a future
crates.io publication. It is versioned as `0.1.0` and pins its internal
`sekiban-core` dependency to the same version while retaining a repository path
for local development. It has not been published to crates.io. Samples should
continue using repository path dependencies until publication is approved.

License: Elastic License 2.0, matching the repository root license.
