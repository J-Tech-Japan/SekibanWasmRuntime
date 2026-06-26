# Rust Crates.io Publication Gate

This gate defines the manual approval and execution plan for the first Rust
crates.io release train. It makes publication executable later, but it does not
publish crates, configure credentials, store tokens, or add release automation.

## Release Train

The first planned Rust crate train is synchronized at version `0.1.0`.

| Publish order | Crate | Role |
| --- | --- | --- |
| 1 | `sekiban-core` | Shared domain traits, DTO primitives, command/query/projector contracts, and runtime payload shapes. |
| 2 | `sekiban-derive` | Procedural macros for Sekiban Rust domain metadata. |
| 3 | `sekiban-wasm` | WASM-side projection export boundary, manifest helpers, and runtime memory helpers. |
| 4 | `sekiban-mv` | WASM-side materialized-view export boundary. |
| 5 | `sekiban-executor` | Typed HTTP executor/client for serialized runtime endpoints. |

The order is dependency-driven. `sekiban-core` must become visible on crates.io
before crates that package exact `sekiban-core = 0.1.0` dependencies can pass a
full publish dry-run. `sekiban-mv` follows `sekiban-wasm` for the same reason.
`sekiban-executor` is last so the remote-client lane is checked after the
runtime endpoint contract and upstream crate visibility are final.

## Non-Goals

- Do not run `cargo publish` in this packet.
- Do not create, store, print, configure, or document any real crates.io token.
- Do not add an automated crates.io publishing workflow for the first release.
- Do not change crate APIs or version numbers unless a later approval gate
  explicitly identifies a blocker.
- Do not publish a GHCR runtime image or change NuGet packaging.

## Approval Boundary

The first release is manual and human-approved. A release owner must explicitly
approve the synchronized `0.1.0` train before any `cargo publish` command is run.
Approval must cover every crate in the train; do not publish a subset as an
experiment.

Required approval checklist:

- Confirm `sekiban-core`, `sekiban-derive`, `sekiban-wasm`, `sekiban-mv`, and
  `sekiban-executor` are the exact crates in scope for `0.1.0`.
- Confirm the version number `0.1.0` is final. crates.io version numbers are
  immutable after publication.
- Confirm each crate name is available on crates.io or already owned by the
  intended publisher account.
- Confirm the crates.io account has the intended organization/user ownership and
  will add any required maintainers after publication.
- Confirm the repository license policy allows `license = "Elastic-2.0"` for
  each published crate.
- Confirm crate-local `README.md`, `description`, `repository`, `license`, and
  `include` metadata are final.
- Confirm release notes are prepared and identify this as the first Rust
  `0.1.0` preview crate train.
- Confirm the public GHCR runtime host compatibility target for the external
  smoke, currently `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3`.
- Confirm no sample or documentation claims the crates are published before the
  publication gate actually runs.

## Credential Boundary

crates.io credentials are outside repository scope. Tokens must be created,
stored, and revoked through the publisher's approved local secret-management
process. They must not be committed, printed in logs, added to GitHub Actions,
or stored in repository files.

Before publishing, the release owner should validate authentication locally with
crates.io tooling that does not expose the token value. If authentication fails,
stop before running any publish command.

## Pre-Publish Verification

Run these checks from the repository root before publication approval is granted:

```bash
cargo test --manifest-path src/wasm-projectors/rust/Cargo.toml --workspace
cargo check --manifest-path src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/Cargo.toml --workspace
for crate in sekiban-core sekiban-derive sekiban-wasm sekiban-mv sekiban-executor; do cargo package --list --manifest-path src/wasm-projectors/rust/$crate/Cargo.toml; done
cargo publish --dry-run --manifest-path src/wasm-projectors/rust/sekiban-core/Cargo.toml
```

Before upstream crates are published, full publish dry-runs for dependent crates
are expected to stop at unpublished-upstream blockers such as `no matching
package named sekiban-core found` or `no matching package named sekiban-wasm
found`. Treat that as expected pre-publication evidence, not a reason to remove
version pins or publish out of order.

## Manual Publish Sequence

Run the publish commands manually, one crate at a time. Do not script the first
release train.

```bash
cargo publish --dry-run --manifest-path src/wasm-projectors/rust/sekiban-core/Cargo.toml
cargo publish --manifest-path src/wasm-projectors/rust/sekiban-core/Cargo.toml
```

After `sekiban-core` is visible on crates.io, verify the package page, owner
metadata, rendered README, license metadata, and docs.rs build status if
available. Then continue:

```bash
cargo publish --dry-run --manifest-path src/wasm-projectors/rust/sekiban-derive/Cargo.toml
cargo publish --manifest-path src/wasm-projectors/rust/sekiban-derive/Cargo.toml
```

Verify `sekiban-derive` visibility and metadata before continuing:

```bash
cargo publish --dry-run --manifest-path src/wasm-projectors/rust/sekiban-wasm/Cargo.toml
cargo publish --manifest-path src/wasm-projectors/rust/sekiban-wasm/Cargo.toml
```

Verify `sekiban-wasm` visibility and metadata before continuing:

```bash
cargo publish --dry-run --manifest-path src/wasm-projectors/rust/sekiban-mv/Cargo.toml
cargo publish --manifest-path src/wasm-projectors/rust/sekiban-mv/Cargo.toml
```

Verify `sekiban-mv` visibility and metadata before continuing:

```bash
cargo publish --dry-run --manifest-path src/wasm-projectors/rust/sekiban-executor/Cargo.toml
cargo publish --manifest-path src/wasm-projectors/rust/sekiban-executor/Cargo.toml
```

After each publish:

- Confirm the crate page resolves on crates.io.
- Confirm version `0.1.0` is visible.
- Confirm ownership/maintainer metadata is correct.
- Confirm the rendered README does not claim unsupported stability.
- Confirm the license is shown as `Elastic-2.0`.
- Confirm dependent crate dry-runs are unblocked only by the upstream crate that
  just became visible.

## Partial Publication Failure Policy

If any publish command fails, stop the train immediately and record the exact
crate, command, error, and whether crates.io accepted the version.

If crates.io accepted a crate version, do not retry with changed source under the
same version. Publishable artifacts for an already accepted version must be
treated as immutable. Continue only after confirming the accepted crate is
visible and the next crate dry-run resolves that upstream version.

If crates.io did not accept the version, fix the blocker in a new reviewable
change before retrying. If the fix changes any package artifact after an earlier
crate was accepted, the release owner must decide whether the remaining crates
can still safely publish as `0.1.0` or whether a new version line is required.

Never yank or republish as a substitute for review. Yanking only changes
resolver availability and does not erase the published artifact.

## Release Notes And Announcement Metadata

Prepare release notes before publishing. The notes should include:

- `0.1.0` as the first synchronized Rust crate train.
- The five crate names and their roles.
- The preview status and expected API-change risk.
- The `Elastic-2.0` license.
- The repository URL.
- The compatible public runtime host image tag for smoke testing.
- A link to the Rust repo-local sample and a note that a separate external
  consumer smoke validates crates.io dependencies after publication.

## External Consumer Smoke Plan

After all five crates are visible on crates.io, run a fresh consumer smoke from
a directory outside this repository checkout. The smoke must not use
repository-local path dependencies.

The external smoke should:

- Create a new Rust workspace or sample project with crates.io dependencies:
  `sekiban-core = "0.1.0"`, `sekiban-derive = "0.1.0"`,
  `sekiban-wasm = "0.1.0"`, `sekiban-mv = "0.1.0"`, and
  `sekiban-executor = "0.1.0"` as applicable.
- Define a small weather-style domain using the derive macros and core command,
  event, state, tag, query, list-query, and projector contracts.
- Build a WASM module using `sekiban-wasm` and, where applicable, `sekiban-mv`
  exports.
- Start the public GHCR runtime host image with Postgres, a generated manifest,
  and the externally built WASM module.
- Use `sekiban-executor` from crates.io to exercise command commit,
  query/tag-state, and list-query paths.
- Verify materialized-view export compatibility where the sample declares an MV.
- Confirm the smoke does not reference `src/wasm-projectors/rust` or any other
  repository-local Rust path dependency.

This smoke can be the same functional scenario as
`docs/samples/public-container-rs-decider.md`, but it must be generated outside
the repository and consume only crates.io Rust packages plus the public GHCR
runtime host image.

## Future Packet Boundaries

Actual publish execution should be a separate, explicitly approved packet. That
packet may run the manual commands above and record exact publication evidence.

Post-publish external consumer smoke may be separate from publish execution if
the release owner wants a smaller irreversible publication packet. If split, the
publish packet must leave the release in a state where the external smoke packet
can use only crates.io dependencies and the public GHCR runtime image.

## Closeout Writeback

This gate is complete when the release owner can follow the approval checklist,
manual command sequence, stop policy, and external smoke plan without consulting
repository-local intent metadata. The next packet should either perform approved
manual publish execution for the `0.1.0` train or run the post-publish external
consumer smoke after publication has completed.
