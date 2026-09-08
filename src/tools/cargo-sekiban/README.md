# cargo-sekiban

`cargo-sekiban` is the Rust-native scaffolder for standalone Sekiban WASM
Decider projects. Install it from crates.io when the package is published, or
build the local package while it is prepared:

```bash
cargo install cargo-sekiban
cargo sekiban new weather-app --template decider --mode registry
```

The package currently ships two modes from the same deterministic, embedded
weather fixture:

- `registry` creates an external-consumer workspace whose five Sekiban
  dependencies are exact crates.io `=0.1.0` requirements. It has no local
  Sekiban path dependencies.
- `dev` creates the public-runtime shape and vendors the required
  `sekiban-core`, `sekiban-derive`, `sekiban-wasm`, `sekiban-mv`,
  `sekiban-executor`, and domain sources under `vendor/`. The generated
  workspace does not require the original repository checkout.

Both generated projects contain the domain, WASM, typed HTTP client, Aspire
AppHost, WASM build/manifest script, no-local-path or vendor guard, and the
channel-owned health/commit/tag/query smoke. Generated artifacts and smoke
reports stay in the generated directory.

## Development verification

Template assets are derived from the maintained Rust samples and internal
crate sources by `scripts/cargo-sekiban/sync-templates.py`. The sync is
deterministic and has a check-only form:

```bash
python3 scripts/cargo-sekiban/sync-templates.py --check
bash scripts/cargo-sekiban/test-generated-workspaces.sh
```

The second command generates both modes in a temporary directory outside this
checkout, runs Cargo metadata/checks and the mode-specific guard, builds WASM
when `wasm32-wasip1` is installed, and runs the generated public-runtime smoke.
The live smoke records `SKIP` only when Docker, the .NET SDK, or the WASM target
is unavailable; a Docker-available runtime failure is a test failure.

The crate is `Prepared/Unpublished`: package inspection and
`cargo publish --dry-run` are validation-only. This slice does not publish to
crates.io, create tags or Releases, use publication credentials, or mutate a
GitHub environment. The independent validation lane uses `rust-cli-v*`; the
SDK crate lane remains `rust-v*`.
