# Rust Crate Metadata Policy

SWR-G053 hardens the public Sekiban Rust crate metadata after the first
crates.io publish so it matches the intentional release quality of the public
NuGet packages. This document records which crates are public distribution
artifacts, the metadata policy, and the intentional differences between the
Cargo and NuGet metadata surfaces.

This packet did **not** publish a new crate version, bump versions, create
release tags/GitHub Releases, or change crates.io credentials. The already
released `0.1.0` crate versions are unchanged.

## Public vs Internal Crates

The workspace under `src/wasm-projectors/rust` contains seven crates. Only five
are public crates.io distribution artifacts; the other two are internal
sample/reference crates marked `publish = false`.

| Crate | Manifest | Classification | crates.io |
| --- | --- | --- | --- |
| `sekiban-core` | `sekiban-core/Cargo.toml` | Public | Published `0.1.0` |
| `sekiban-derive` | `sekiban-derive/Cargo.toml` | Public | Published `0.1.0` |
| `sekiban-wasm` | `sekiban-wasm/Cargo.toml` | Public | Published `0.1.0` |
| `sekiban-mv` | `sekiban-mv/Cargo.toml` | Public | Published `0.1.0` |
| `sekiban-executor` | `sekiban-executor/Cargo.toml` | Public | Published `0.1.0` |
| `sekiban-wasm-domain` | `domain/Cargo.toml` | Internal (`publish = false`) | Never published |
| `sekiban-wasm-projector` | `wasm-projector/Cargo.toml` | Internal (`publish = false`) | Never published |

The two internal crates are repo-local sample/reference code (a sample domain
and the `cdylib` projector module). They are explicitly `publish = false` so a
future release train cannot accidentally treat them as public distribution
artifacts.

## Shared Metadata via `[workspace.package]`

Shared release metadata is centralized in `[workspace.package]` in
`src/wasm-projectors/rust/Cargo.toml` and inherited by each public crate with
`<field>.workspace = true`. This keeps the public crate metadata consistent and
drift-free, mirroring how the NuGet packages centralize shared metadata in
`Directory.Build.props`.

Inherited (shared) fields:

| Field | Value |
| --- | --- |
| `authors` | `["J-Tech Japan, Inc."]` |
| `edition` | `2021` |
| `license` | `Elastic-2.0` (SPDX expression) |
| `homepage` | `https://github.com/J-Tech-Japan/SekibanWasmRuntime` |
| `repository` | `https://github.com/J-Tech-Japan/SekibanWasmRuntime` |
| `keywords` | `["sekiban", "dcb", "event-sourcing", "wasm", "wasi"]` |

The internal crates intentionally do **not** inherit this public metadata.

## Per-Crate Metadata

Crate-specific fields stay in each crate manifest so each package keeps its own
identity:

- `description` — crate-specific one-line purpose.
- `readme` — crate-local `README.md` (crates.io renders package-relative READMEs,
  so this is kept per-crate rather than inherited).
- `documentation` — explicit `https://docs.rs/<crate>` URL per crate.
- `categories` — crate-specific crates.io category slugs:
  - `sekiban-core`: `data-structures`, `wasm`
  - `sekiban-derive`: `development-tools::procedural-macro-helpers`, `rust-patterns`
  - `sekiban-wasm`: `wasm`, `api-bindings`
  - `sekiban-mv`: `wasm`, `data-structures`
  - `sekiban-executor`: `web-programming::http-client`, `api-bindings`
- `include` — explicit `["Cargo.toml", "README.md", "src/**"]` package boundary so
  only source, the crate README, and the manifest are packaged.

## Intentional Cargo vs NuGet Differences

The Cargo and NuGet metadata surfaces are aligned where Cargo supports an
equivalent field, with these intentional differences:

| Concern | NuGet (`Directory.Build.props`) | Cargo (`[workspace.package]` + crate) |
| --- | --- | --- |
| License | `PackageLicenseFile = LICENSE` (bundles the Elastic License 2.0 file) | `license = "Elastic-2.0"` SPDX expression; the license file is not bundled because Cargo accepts the SPDX id directly. Both reference Elastic License 2.0. |
| Project URL | `PackageProjectUrl` | `homepage` + `repository` (Cargo separates the two; both point at the repository) |
| Docs | docs.rs has no NuGet equivalent | `documentation = "https://docs.rs/<crate>"` per crate |
| Tags / keywords | `PackageTags = sekiban;dcb;wasm;wasi;event-sourcing` | `keywords = ["sekiban", "dcb", "event-sourcing", "wasm", "wasi"]` (same five terms; Cargo caps keywords at five) |
| Categories | no NuGet equivalent | crate-specific crates.io category slugs |
| Version | `1.0.0-preview.1` (NuGet runtime packages) | `0.1.0` (already published Rust line; not bumped here) |

The package version lines are independent by design: the public Rust crate train
is on `0.1.0` while the public NuGet runtime packages are on `1.0.0-preview.1`.

## Verification

Run from the repository root:

```bash
cargo metadata --manifest-path src/wasm-projectors/rust/Cargo.toml --format-version 1
cargo test --manifest-path src/wasm-projectors/rust/Cargo.toml --workspace
cargo check --manifest-path src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Rs/Cargo.toml --workspace
for crate in sekiban-core sekiban-derive sekiban-wasm sekiban-mv sekiban-executor; do
  cargo package --list --manifest-path src/wasm-projectors/rust/$crate/Cargo.toml
done
rg -n "^(authors|homepage|documentation|repository|license|license-file|readme|keywords|categories|include|exclude|description)\s*=" src/wasm-projectors/rust -g Cargo.toml
git diff --check
```

`cargo metadata` confirms each public crate resolves the inherited shared fields
plus its per-crate `description`, `documentation`, and `categories`. The
`cargo package --list` inventory for each public crate contains only
`Cargo.toml`, `README.md`, `src/**`, and Cargo's auto-generated packaging files
(`.cargo_vcs_info.json`, `Cargo.lock`, `Cargo.toml.orig`) — no unexpected
internal files.
