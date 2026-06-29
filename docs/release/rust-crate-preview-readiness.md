# Rust Crate Public Release Readiness

This inventory prepares the Rust crates under `src/wasm-projectors/rust` for a
future crates.io decision. It does not publish crates, configure credentials, or
add release automation.

## Current Decision

The Rust crates are still in the repo-local library phase. Samples may depend on
them through path dependencies, and the public runtime host container remains an
independent artifact lane. A crates.io release should be a later packet after the
external-consumer smoke and actual publication gates below are closed.

SWR-G050 completed the metadata-only release-prep step. The five candidate
crates now have package metadata, crate-local READMEs, crate-level preview API
docs, and exact version requirements on internal path dependencies. No
`cargo publish` command was run.

SWR-G051 added the crates.io publication gate in
[`docs/release/rust-crates-publication-gate.md`](rust-crates-publication-gate.md).
SWR-G052 supersedes the local-token/manual-publication direction with the
manual-only GitHub Actions workflow
`.github/workflows/release-rust-crates-first-publish.yml`. That gate defines the
approval checklist, protected `crates-io-release` environment, `publish: true`
switch, dependency publish order, partial-failure policy, and post-publication
external consumer smoke plan. The crates remain unpublished until that workflow
is explicitly approved and dispatched in publish mode.

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
| `package.version` | Release all five together on the synchronized `0.1.0` line so inter-crate dependency pins are simple. |
| `license` | Use SPDX `Elastic-2.0`, matching the repository root Elastic License 2.0 policy. |
| `description` | Use crate-specific one-line descriptions matching the candidate matrix purpose column. |
| `repository` | `https://github.com/J-Tech-Japan/SekibanWasmRuntime` |
| `readme` | Use crate-local `README.md` files because crates.io renders package-relative README paths. |
| `documentation` | Add docs.rs URLs after names and versions are final, or omit until the first release exists. |
| `include` / `exclude` | Use explicit `include = [\"Cargo.toml\", \"README.md\", \"src/**\"]`; license metadata is expressed through SPDX `license`. |
| Public API docs | Use module-level crate docs that explain which traits/macros are preview public boundary versus internal implementation detail. |

## Applied Release-Prep Metadata

SWR-G050 applies one synchronized first public version strategy across all five
candidate crates:

- Version line: `0.1.0`
- Internal dependency pins: exact `=0.1.0` requirements while retaining local
  `path` entries for repository development
- Repository metadata: `https://github.com/J-Tech-Japan/SekibanWasmRuntime`
- License metadata: SPDX `Elastic-2.0`, matching the repository root Elastic
  License 2.0 policy
- README strategy: crate-local `README.md` files for every candidate crate
- Include policy: package only `Cargo.toml`, crate-local `README.md`, and
  `src/**`

The dependency pins are intentionally exact so a future release train can
publish upstream crates first, then package dependent crates with the same
version line.

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

### SWR-G050 Package Dry-Run Evidence

Commands were run from the repository root on June 26, 2026 after metadata,
crate-local READMEs, crate-level docs, and exact versioned path dependencies
were added.

| Crate | `cargo package --list` | `cargo package --no-verify` | Result |
| --- | --- | --- | --- |
| `sekiban-core` | Passed; listed 15 package files including `Cargo.toml`, `README.md`, and `src/**`. | Passed; packaged 15 files, 31.6 KiB. | Ready for a future first publish gate. |
| `sekiban-derive` | Passed; listed 11 package files including `Cargo.toml`, `README.md`, and `src/**`. | Failed. | Blocked until `sekiban-core = 0.1.0` exists on crates.io. |
| `sekiban-wasm` | Passed; listed 13 package files including `Cargo.toml`, `README.md`, and `src/**`. | Failed. | Blocked until `sekiban-core = 0.1.0` exists on crates.io. |
| `sekiban-mv` | Passed; listed 11 package files including `Cargo.toml`, `README.md`, and `src/**`. | Failed. | Blocked until `sekiban-wasm = 0.1.0` exists on crates.io. |
| `sekiban-executor` | Passed; listed 6 package files including `Cargo.toml`, `README.md`, and `src/lib.rs`. | Failed. | Blocked until `sekiban-core = 0.1.0` exists on crates.io. |

The dependent-crate `cargo package --no-verify` failures are now different from
the earlier path-only manifest error. Versioned path dependencies are present,
so Cargo strips the local `path` entry from the packaged manifest and tries to
resolve the exact version from crates.io. Because this packet deliberately does
not publish crates, dependent crates report the expected unpublished-upstream
blocker:

```text
no matching package named `sekiban-core` found
location searched: crates.io index
required by package `sekiban-derive v0.1.0`
```

```text
no matching package named `sekiban-core` found
location searched: crates.io index
required by package `sekiban-wasm v0.1.0`
```

```text
no matching package named `sekiban-wasm` found
location searched: crates.io index
required by package `sekiban-mv v0.1.0`
```

```text
no matching package named `sekiban-core` found
location searched: crates.io index
required by package `sekiban-executor v0.1.0`
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

- Publish is still deferred; no crates.io credential value has been stored, no
  workflow has been dispatched in publish mode, and no `cargo publish` command
  has been run from this packet.
- Dependent crates cannot complete `cargo package --no-verify` until their
  upstream internal crates exist on crates.io at exact version `0.1.0`.
- Add a future external Rust consumer smoke after crates are actually published.

## Recommended Next Packet

Use the publication-gate document to request explicit human approval for a
`release-rust-crates-first-publish` workflow dispatch with `publish: true`.
Actual crates.io publication and the external consumer smoke remain separate
future packets. Do not add automatic publish-on-tag behavior until the protected
first-release workflow gate has been approved and completed.

## SWR-G055 crates.io Consumer Sample

After the first publish completed, SWR-G055 added
`src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider` as the external Rust
consumer sample. It consumes `sekiban-core`, `sekiban-derive`, `sekiban-wasm`,
`sekiban-mv`, and `sekiban-executor` from crates.io at exact `=0.1.0`
requirements, owns its domain code in the sample, and avoids both repository
local `src/wasm-projectors/rust/sekiban-*` paths and the unpublished
`sekiban-wasm-domain` helper crate.

Run the sample boundary check with:

```bash
bash src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider/scripts/verify-no-local-sekiban-paths.sh
```

See
[`rust-crates-io-consumer-sample.md`](rust-crates-io-consumer-sample.md)
for the consumer sample details and closeout note.
