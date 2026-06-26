# sekiban-derive

`sekiban-derive` provides procedural derive macros for Sekiban Rust domain
declarations, including events, states, tags, projectors, and commands.

Use this crate when authoring Rust domain types that should implement the
Sekiban contracts from `sekiban-core`. The intended public API boundary is the
exported derive macro set; generated helper details are preview implementation
detail.

Release status: this crate is a repo-local release candidate for a future
crates.io publication. It is versioned as `0.1.0` to match the coordinated first
Rust release line, but it has not been published to crates.io. Samples should
continue using repository path dependencies until publication is approved.

License: Elastic License 2.0, matching the repository root license.
