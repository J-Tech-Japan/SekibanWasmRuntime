# Rust Crates.io Publication Gate

This gate defines the manual approval and execution plan for the first Rust
crates.io release train. It makes publication executable later, but it does not
publish crates, configure credentials, store tokens, or run release automation.

SWR-G052 supersedes the earlier local-token/manual-publication direction. The
accepted first-release path is the protected GitHub Actions workflow
`.github/workflows/release-rust-crates-first-publish.yml`. Do not use local
`cargo login`, local token files, or local `CARGO_REGISTRY_TOKEN` for this first
release.

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
- Do not run the GitHub Actions workflow in publish mode from this packet.
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

## GitHub Actions First-Publish Boundary

The first release must be dispatched from GitHub Actions, not a maintainer
machine. The workflow is manual-only:

- Workflow: `release-rust-crates-first-publish`
- Trigger: `workflow_dispatch` only
- Protected environment: `crates-io-release`
- Publish switch: input `publish: true`
- Expected version input: `expected_version: 0.1.0`

The `crates-io-release` environment must be configured in GitHub before publish
mode is used:

- Add required reviewers for the release owners.
- Add an environment secret named `CARGO_REGISTRY_TOKEN`.
- Scope the crates.io token to the intended publish operation and revoke/rotate
  it after the first release according to the publisher's secret policy.
- Do not create, store, print, or commit the token in this repository.

The workflow reads `CARGO_REGISTRY_TOKEN` only inside the publish step. Check mode
does not read the secret. GitHub masks secret values in logs, and the workflow
does not echo the token.

Trusted Publishing is not the selected first-release strategy for this packet.
The crates.io Trusted Publishing flow currently still requires a publisher setup
that is not suitable as the bootstrap path for these brand-new crates in this
repo. After the five crates exist on crates.io, a later packet may replace the
environment secret with Trusted Publishing/OIDC if crates.io and the repository
publisher configuration support it.

## Pre-Publish Verification

Run these checks from the repository root before publication approval is granted,
or run the workflow with `publish: false` for the GitHub-hosted check path:

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

## GitHub Actions Publish Sequence

The first release is launched by manually dispatching
`release-rust-crates-first-publish` from the GitHub Actions UI:

1. Select branch `main`.
2. Set `expected_version` to `0.1.0`.
3. First run with `publish: false` to execute tests, sample check, package file
   inventory, and the `sekiban-core` dry-run.
4. Review the run logs and confirm no sample or document claims crates are
   already published.
5. Re-run with `publish: true`.
6. Approve the `crates-io-release` environment when GitHub prompts reviewers.

In publish mode, the workflow stops immediately on the first failure. For each
crate it runs `cargo publish --dry-run` and then `cargo publish`, in dependency
order:

1. `sekiban-core`
2. `sekiban-derive`
3. `sekiban-wasm`
4. `sekiban-mv`
5. `sekiban-executor`

After each publish:

- Confirm the crate page resolves on crates.io.
- Confirm version `0.1.0` is visible.
- Confirm ownership/maintainer metadata is correct.
- Confirm the rendered README does not claim unsupported stability.
- Confirm the license is shown as `Elastic-2.0`.
- Confirm dependent crate dry-runs are unblocked only by the upstream crate that
  just became visible.

Do not run any local `cargo publish` command unless a future emergency recovery
packet explicitly authorizes it. The normal first-release lane is GitHub Actions
plus the protected environment approval.

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

Actual publish execution should be a separate, explicitly approved dispatch of
the GitHub Actions workflow above. That packet should record the workflow run,
environment approval, exact crate versions, crates.io package URLs, and whether
each crate was accepted.

Post-publish external consumer smoke may be separate from publish execution if
the release owner wants a smaller irreversible publication packet. If split, the
publish packet must leave the release in a state where the external smoke packet
can use only crates.io dependencies and the public GHCR runtime image.

## Closeout Writeback

This gate is complete when the release owner can follow the approval checklist,
GitHub Actions dispatch sequence, stop policy, and external smoke plan without
consulting repository-local intent metadata. The next packet should either
perform approved workflow dispatch for the `0.1.0` train or run the post-publish
external consumer smoke after publication has completed.
