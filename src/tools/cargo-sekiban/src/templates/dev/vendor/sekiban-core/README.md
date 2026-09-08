# sekiban-core

`sekiban-core` contains the shared Rust domain contracts for Sekiban WASM
projection modules: commands, events, state payloads, tags, projectors, queries,
registry helpers, and serializable runtime payloads.

Use this crate when writing Rust domain code or Rust runtime helpers that need
the transport-neutral Sekiban contracts. The intended public API boundary is the
crate root and `prelude` re-exports; lower-level module internals remain preview
implementation detail.

Release status: this crate is a repo-local release candidate for a future
crates.io publication. It is versioned as `0.1.0` to keep the first coordinated
Rust release line explicit, but it has not been published to crates.io. Samples
should continue using repository path dependencies until publication is approved.

License: Elastic License 2.0, matching the repository root license.
