# cargo-sekiban Rust Decider scaffolder

Status: **Prepared/Unpublished**. The package and its validation lane are
ready for a separate human-gated crates.io decision; this repository does not
publish cargo-sekiban.

## Usage

After the package is published:

    cargo install cargo-sekiban
    cargo sekiban new weather-app --template decider --mode registry

The command accepts registry and dev modes:

- registry creates an external-consumer workspace. Every Sekiban dependency is
  pinned to exact crates.io =0.1.0; the generated manifests contain no
  repository-local Sekiban path.
- dev creates the same weather Decider against the public runtime container
  and includes the required source crates under vendor/. Its Cargo workspace
  is self-contained and can be checked after the original checkout is
  unavailable.

--path selects an exact output directory, and generation refuses a non-empty
directory unless --force is supplied. The CLI validates the project name,
template (decider), and mode before writing anything.

## Bundled template provenance

The embedded registry and dev assets are derived from:

- src/samples/Sekiban.Dcb.WasmRuntime.CratesIo.RsDecider
- src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider
- the six required crates under src/wasm-projectors/rust for dev vendoring

scripts/cargo-sekiban/sync-templates.py rewrites the sample trees into
standalone assets, replaces project-name spellings with render tokens, and
copies files in sorted order. It excludes build output and generated
Cargo.lock files. The check form compares the expected tree byte-for-byte and
mode-for-mode:

    python3 scripts/cargo-sekiban/sync-templates.py --check

The published crate embeds these checked-in assets at compile time through its
standard-library-only build script. Generation does not use Node, an npm
package, a network download, or runtime template fetch.

## Generated verification

The source samples retain the serialized DCB V1 weather shape and the
channel-owned smoke: health/readiness, typed command commit, tag-state
readback, list/count query proof, and materialized-view catch-up. The generated
verification is intentionally outside the repository checkout:

    bash scripts/cargo-sekiban/test-generated-workspaces.sh

That check runs the deterministic sync check, generates both modes under a
temporary external directory, runs the registry no-local-path guard and dev
vendor guard, and performs cargo metadata plus cargo check --workspace. When
the active compiler can use wasm32-wasip1, it also builds each module. Each
generated smoke writes a report and must state PASS or SKIP. A live
Docker/.NET/runtime failure is not converted into a skip; unavailable
Docker/.NET/WASI prerequisites are recorded as the permitted live-smoke skip.

The portable conformance/serialized-dcb-v1 suite is not part of this tool and
is not modified by this lane.
