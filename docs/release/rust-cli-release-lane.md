# Rust CLI release lane

Status: **Prepared/Unpublished** for cargo-sekiban version 0.1.0.

This is a validation-only lane for the Cargo subcommand in
src/tools/cargo-sekiban. It is independent from the published Rust SDK crates
and their rust-v* lane.

## Tag and trigger

The lane owns rust-cli-v<version> (for example, rust-cli-v0.1.0) through
.github/workflows/release-rust-cli.yml. Pull requests touching the CLI,
its proof scripts, or this workflow run the same path-scoped validation. A
published GitHub Release with another prefix skips the job. Workflow dispatch
accepts a bare expected version for local/operator validation.

The workflow performs:

- exact package-version validation;
- deterministic template synchronization;
- Cargo fmt, clippy, and tests;
- the stable toolchain's `wasm32-wasip1` target installation and real module
  builds;
- external-directory registry/dev generation and workspace checks;
- channel-owned runtime smoke with uploaded generated reports; only the exact
  `Docker is not available.` reason is permitted as a pull-request smoke skip;
- cargo package --list; and
- cargo publish --dry-run.

The workflow has no publish job, crates.io credential, GitHub environment,
tag/release creation, or Trusted Publishing configuration. An operator may
use the evidence to make a separate publication decision later.

## Compatibility evidence

The generated projects target the registry-published Rust SDK train at exact
0.1.0 and the registry-verified runtime image
ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.7. The
SWR-G091 / #293 proof records:

- registry mode: exact dependency pins, no-local-path guard, metadata/check,
  and standalone generation;
- dev mode: all six required sources vendored and standalone Cargo
  metadata/check;
- both modes: the weather serialized DCB V1 fixture and channel-owned smoke
  assets; and
- package inventory plus publish dry-run are validation-only.

The generated runtime smoke writes a report. If the local active compiler
lacks wasm32-wasip1, the WASM/live-runtime portion is recorded as SKIP;
otherwise a failed module build or available runtime smoke fails validation.
No TypeScript/npm generator, SDK crate version, portable conformance suite, or
shared release environment is changed by this lane.
