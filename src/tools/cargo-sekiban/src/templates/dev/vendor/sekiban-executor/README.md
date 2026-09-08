# sekiban-executor

`sekiban-executor` contains the Rust HTTP executor client for serialized Sekiban
WASM runtime endpoints. It provides typed command and query execution helpers,
remote command context support, tag projector resolution, transport options, and
runtime response DTOs.

Use this crate from Rust clients or smoke tests that call a running Sekiban WASM
runtime host over HTTP. The intended public API boundary is the remote executor,
command context, resolver, request/response DTOs, and error surface; transport
internals remain preview implementation detail.

Release status: this crate is a repo-local release candidate for a future
crates.io publication. It is versioned as `0.1.0` and pins its internal
`sekiban-core` dependency to the same version while retaining a repository path
for local development. It has not been published to crates.io. Samples should
continue using repository path dependencies until publication is approved.

License: Elastic License 2.0, matching the repository root license.
