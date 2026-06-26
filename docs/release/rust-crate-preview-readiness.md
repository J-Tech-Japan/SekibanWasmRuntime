# Rust Crate Public Release Readiness

This inventory prepares the Rust crates under `src/wasm-projectors/rust` for a
future crates.io decision. It does not publish crates, configure credentials, or
add release automation.

## Current Decision

The Rust crates are still in the repo-local library phase. Samples may depend on
them through path dependencies, and the public runtime host container remains an
independent artifact lane. A crates.io release should be a later packet after the
metadata, version pinning, README, and dependency-transition blockers below are
closed.

## Candidate Matrix

| Crate | Classification | Purpose | Public API boundary | Dependency role | Sample/runtime relationship |
| --- | --- | --- | --- | --- | --- |
| `sekiban-core` | publish-later | Shared domain traits and DTO primitives for events, states, tags, commands, queries, projectors, and domain registration. | `prelude`, trait contracts, error types, registry/domain macros, and serializable runtime payload shapes. | Root Rust crate; no internal path dependencies. | Used by Rust domain crates, the executor, WASM boundary crates, and test/sample domains. |
| `sekiban-derive` | publish-later | Procedural macros that derive Sekiban Rust domain metadata. | Derive macros for command, event, state, tag, and projector declarations. | Depends on proc-macro ecosystem; has a dev-only path dependency on `sekiban-core`. | Developer ergonomics for Rust domain source; not loaded by the runtime container. |
| `sekiban-executor` | publish-later | Typed HTTP executor/client over the serialized runtime endpoints. | `RemoteSekibanExecutor`, command context, tag projector resolver, command/query execution helpers, and HTTP transport options/errors. | Depends on `sekiban-core`; must pin a crates.io version before packaging. | Used by Rust clients and smoke tests that call a running runtime host container. |
| `sekiban-wasm` | publish-later | WASM-side projection export boundary and manifest/runtime memory helpers. | `export_domain!`, WASM ABI exports, instance/manifest/memory compatibility types. | Depends on `sekiban-core`; must pin a crates.io version before packaging. | Compiled into Rust WASM modules loaded by the runtime host. |
| `sekiban-mv` | publish-later | WASM-side materialized-view export boundary. | MV DTOs, `WasmMvProjector`, `MvParamBuilder`, host-backed query port, and `export_mv!`. | Depends on `sekiban-wasm`; must pin a crates.io version before packaging. | Compiled into Rust WASM modules that expose `mv_metadata`, `mv_initialize`, and `mv_apply_event`. |

No candidate should be classified as publish-now yet. All five are useful public
artifact candidates, but the current manifests lack required package metadata and
the dependent crates still use repo-local path-only internal dependencies.

## Required Package Metadata

Use the same metadata strategy for every publishable crate:

| Field | Proposed value / policy |
| --- | --- |
| `package.name` | Keep the current crate names: `sekiban-core`, `sekiban-derive`, `sekiban-executor`, `sekiban-wasm`, `sekiban-mv`. |
| `package.version` | Release all five together on a coordinated `0.1.0-preview.1` or `0.1.0` line. Prefer one synchronized first public version so inter-crate dependency pins are simple. |
| `license` | Add the repository-approved SPDX license after confirming the root repository license policy. Do not guess this before release. |
| `description` | Add crate-specific one-line descriptions matching the candidate matrix purpose column. |
| `repository` | `https://github.com/J-Tech-Japan/SekibanWasmRuntime` |
| `readme` | Add crate-local `README.md` files or use a stable workspace-relative README strategy. crates.io renders package-relative README paths, so crate-local READMEs are the least fragile option. |
| `documentation` | Add docs.rs URLs after names and versions are final, or omit until the first release exists. |
| `include` / `exclude` | Prefer explicit `include = [\"Cargo.toml\", \"README.md\", \"src/**\", \"LICENSE*\"]` after crate-local READMEs/licenses exist. Avoid packaging samples, generated `target/`, and runtime artifacts. |
| Public API docs | Add module-level crate docs before publish. The first release should explain which traits/macros are stable preview contract versus internal implementation detail. |

## Publish Order

Use one release train and publish in dependency order:

1. `sekiban-core`
2. `sekiban-derive`
3. `sekiban-wasm`
4. `sekiban-mv`
5. `sekiban-executor`

`sekiban-derive` can be published after `sekiban-core` even though its
`sekiban-core` dependency is dev-only. `sekiban-mv` follows `sekiban-wasm`
because its packaged dependency currently points at that crate. `sekiban-executor`
can be published after `sekiban-core`; it is listed last because it represents
the public remote-client lane and should be checked against the final runtime
endpoint contract.

## Path Dependency Transition

Before publishing dependent crates, change internal dependencies from path-only
to versioned path dependencies during the release-prep PR:

```toml
sekiban-core = { version = \"=0.1.0\", path = \"../sekiban-core\" }
sekiban-wasm = { version = \"=0.1.0\", path = \"../sekiban-wasm\" }
```

For the release commit, either keep both `version` and `path` so local workspace
development still works, or use a release-only packaging patch that removes
`path` after all upstream crates are available on crates.io. Cargo removes
`path` from the packaged manifest and uses the version requirement, so the
version must be exact and must match the crates already published.

Samples should continue to use path dependencies until the public crates are
actually published. After publication, add a separate external-consumer smoke
that uses crates.io dependencies without repository-local paths.

## Packaging Dry-Run Evidence

Commands were run from the repository root on June 26, 2026.

| Crate | `cargo package --list` | `cargo package --no-verify` | Result |
| --- | --- | --- | --- |
| `sekiban-core` | Passed; listed `Cargo.toml`, `Cargo.lock`, `src/**`, and cargo VCS metadata. | Passed; packaged 14 files, 29.8 KiB. | Archive can be created, but manifest metadata is incomplete. |
| `sekiban-derive` | Passed; listed `Cargo.toml`, `Cargo.lock`, `src/**`, and cargo VCS metadata. | Passed; packaged 10 files, 12.5 KiB. | Archive can be created, but manifest metadata is incomplete. |
| `sekiban-executor` | Passed; listed `Cargo.toml`, `Cargo.lock`, and `src/lib.rs`. | Failed. | Blocked because dependency `sekiban-core` has a path but no version requirement. |
| `sekiban-wasm` | Passed; listed `Cargo.toml`, `Cargo.lock`, `src/**`, and cargo VCS metadata. | Failed. | Blocked because dependency `sekiban-core` has a path but no version requirement. |
| `sekiban-mv` | Passed; listed `Cargo.toml`, `Cargo.lock`, `src/**`, and cargo VCS metadata. | Failed. | Blocked because dependency `sekiban-wasm` has a path but no version requirement. |

All five `cargo package --list` runs emitted the same metadata warning:

```text
warning: manifest has no description, license, license-file, documentation, homepage or repository
```

The dependent-crate packaging failures reported:

```text
all dependencies must have a version requirement specified when packaging.
dependency `<crate>` does not specify a version
```

## Verification Evidence

The following checks passed:

```bash
cargo test --manifest-path src/wasm-projectors/rust/Cargo.toml --workspace
cargo check --manifest-path src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/Cargo.toml --workspace
```

The sample check completed with the existing warning:

```text
warning: function `ensure_command_success` is never used
```

## Blockers

- Confirm and add the repository-approved SPDX license metadata for Rust crates.
- Add descriptions, repository metadata, README strategy, and include/exclude
  policy to each candidate manifest.
- Add crate-level docs that state preview stability and public API boundaries.
- Add exact inter-crate version requirements before packaging dependent crates.
- Decide whether the first Rust crate line is `0.1.0-preview.1` or `0.1.0`.
- Add a future external Rust consumer smoke after crates are actually published.

## Recommended Next Packet

Prepare a metadata-only Rust crate release-prep PR: add crate-local READMEs,
crate-level docs, SPDX license metadata, repository/description/include fields,
and versioned path dependencies. Re-run `cargo package --no-verify` for all five
crates. Do not add credentials or run `cargo publish` until that PR is reviewed.
