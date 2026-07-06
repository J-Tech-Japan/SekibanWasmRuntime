# `create-sekiban-wasm`

`npx create-sekiban-wasm` is the cross-language onboarding CLI for the
Sekiban WASM Runtime, covering the languages the C# `dotnet new
sekiban-wasm-decider` template (SWR-G068) does not: Rust, TypeScript, Go,
Swift, and MoonBit. It scaffolds a project directly from the monorepo's
maintained sample shapes -- it never invents a new project layout and never
fetches templates over the network; everything it generates is bundled into
the npm package at pack time (`npm run sync-templates`, wired as the
`pretest`/`prepack` lifecycle scripts).

Package: `src/tools/create-sekiban-wasm` (npm name `create-sekiban-wasm`,
version `0.1.0`; not yet published -- see
[Availability and publish status](#availability-and-publish-status)).

## Usage

```bash
npx create-sekiban-wasm --language rust
npx create-sekiban-wasm --language ts --dir ./my-weather-app
npx create-sekiban-wasm --language all --dir ./sekiban-samples
```

Omit `--language` in an interactive terminal and the CLI prompts for one;
in a non-interactive context (CI, piped stdin) it exits with an error asking
for the flag explicitly.

| Option | Default | Effect |
| --- | --- | --- |
| `--language <id>` | prompted | One of `rust`, `ts`, `go`, `swift`, `moonbit`, or `all`. Unknown values are rejected with a clear message. |
| `--mode <mode>` | `registry` | See [Registry mode vs. dev mode](#registry-mode-vs-dev-mode). |
| `--dir <path>` | `./<language>-sekiban-wasm` | Output directory. For `--language all`, defaults to `./sekiban-wasm-all` with one subdirectory per language. |
| `--force` | off | Allow generating into a non-empty directory (refused otherwise). |

## Registry mode vs. dev mode

- **`registry` mode** generates an external-consumer sample: the project
  depends on the published Sekiban package for that language only, at an
  exact version, with **no** local path or workspace-relative dependency of
  any kind. Every registry-mode template ships the same
  `scripts/verify-no-local-sekiban-paths.sh` guard its monorepo source
  sample has, adapted to run standalone (see
  [Portability adjustments](#portability-adjustments-at-pack-time)). The
  generated `README.md` states the mode at the top.
- **`dev` mode** is a local build against the public runtime container from
  repository-local source, mirroring samples like
  `Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider`. **Available for Rust
  in 0.1.0**: the generator vendors the monorepo-internal
  `src/wasm-projectors/rust` crates (`sekiban-core`, `sekiban-derive`,
  `sekiban-mv`, `sekiban-wasm`, `sekiban-executor`, `domain`) into a
  `vendor/` directory inside the generated project and rewrites the sample's
  local-path dependencies to point there, so `cargo check --workspace`
  succeeds standalone with no monorepo checkout at all. For the other four
  languages, `--mode dev` reports a clear "not available" message and
  generates nothing -- it never produces broken output. The reason is
  simpler than a bundling-complexity tradeoff: **no local-container dev-mode
  sample exists yet in the monorepo for TypeScript, Go, Swift, or MoonBit**
  (only `PublicContainer.RsDecider` provides this shape today), so there is
  nothing to bundle. This is tracked as a follow-up, not a silent gap -- see
  [Closeout learning](#closeout-learning).

## Per-language availability (0.1.0)

| Language | Registry mode | Dev mode |
| --- | --- | --- |
| Rust | Available -- depends on crates.io `sekiban-*` at exact `=0.1.0`. **Already published**; the generated project builds and runs standalone today. | **Available** -- vendors `src/wasm-projectors/rust` into `vendor/`; `cargo check --workspace` succeeds standalone today. |
| TypeScript | Available -- depends on `@sekiban/ts`/`@sekiban/as-wasm` at exact npm `0.1.0`. Not published yet (SWR-G058); `npm install` 404s until it is. | Not available -- no dev-mode sample exists in the monorepo yet. |
| Go | Available -- requires the published module `github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go` at `v0.1.0`. Tag not published yet; `go build` fails to resolve it until it is. | Not available -- no dev-mode sample exists in the monorepo yet. |
| Swift | Available -- depends on the public `github.com/J-Tech-Japan/sekiban-swift` mirror at exact `0.1.0`. Mirror visibility is a separate human-gated step. | Not available -- no dev-mode sample exists in the monorepo yet. |
| MoonBit | Available -- depends on `sekiban/sekiban-wasm-runtime` and `sekiban/sekiban-client` as mooncakes.io registry packages. Not published yet. | Not available -- no dev-mode sample exists in the monorepo yet. |

Every generated project also provisions the **public GHCR runtime
container** (`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`, default tag
`1.0.0-preview.3`, override with `SAMPLE_RUNTIME_IMAGE_TAG`) through a
sample-owned Aspire AppHost, exactly like its source sample.

## Portability adjustments at pack time

The monorepo sample scripts assume they live at
`src/samples/<Name>/scripts/*.sh` inside the full repository checkout (they
resolve their repo root four directories up and reference
`src/samples/<Name>` explicitly). A generated project is a standalone
directory, not a monorepo checkout, so `npm run sync-templates`
(`scripts/sync-templates.mjs`) rewrites, for every bundled `.sh` script and
`AppHost/Program.cs`:

- the four-directories-up `ROOT=` resolution to a one-directory-up
  resolution (`scripts/` sits directly under the generated project root);
- the `SAMPLE_DIR="src/samples/<Name>"` prefix to `"."`;
- any literal `src/samples/<Name>/` path segment embedded in log/error
  messages or the README, so printed repair instructions read
  `bash scripts/build-wasm.sh` instead of the monorepo-relative form.

It also excludes dev-time-only monorepo overlays that would break outside
the monorepo checkout entirely: Go's `go.work`/`go.work.sum` workspace
overlay (its `replace` directive points at monorepo-local
`src/lib/sekiban-go`), Swift's `.build`/`.swiftpm` directories, and the
usual build-artifact directories (`node_modules`, `bin`, `obj`, `target`,
`artifacts`, `reports`, `_build`). Excluding the overlay means a generated
Go project attempts **real** module resolution (failing cleanly with a
not-found error until the tag publishes) instead of silently succeeding
through a local override that would never work for an actual npx user.
`sync-templates.mjs` self-checks its own output (`.sh`, `.cs`, and `.md`
files) for `src/samples/` or `../../../..` residue and fails loudly if any
adjustment was missed.

For `rust`'s `dev` mode specifically, `sync-templates.mjs` also vendors
`src/wasm-projectors/rust`'s `sekiban-core`/`sekiban-derive`/`sekiban-mv`/
`sekiban-wasm`/`sekiban-executor`/`domain` crates into `vendor/` and
rewrites `Wasm/Cargo.toml`/`Client/Cargo.toml`'s
`path = "../../../wasm-projectors/rust/<crate>"` references to
`path = "../vendor/<crate>"`. The vendored crates' own inter-crate
dependencies are all plain `../<crate>` siblings, so preserving that layout
under `vendor/` needs no further rewriting. The publishable vendored crates
declare `edition`/`authors`/`license`/etc. as `<field>.workspace = true`,
inherited from `wasm-projectors/rust/Cargo.toml`'s `[workspace.package]`
table -- `sync-templates.mjs` copies that table into the generated
project's own root `Cargo.toml` too, or `cargo` fails with
`workspace.package.edition was not defined`.

### Standalone-mode guard for monorepo-only pre-publish flags

Several source samples have an *opt-in* monorepo-only pre-publish dry-run
mode: TypeScript's `SEKIBAN_NPM_MODE=tarball` (packs `@sekiban/ts`/
`@sekiban/as-wasm` from `src/lib`), Go's `smoke.sh --local-module` (uses the
excluded `go.work` overlay), Swift's `smoke.sh --local-package` (SwiftPM
dependency mirroring against a staged monorepo tree), and MoonBit's
`build-wasm.sh`/`smoke.sh --local-packages` (a staged copy with path deps on
`src/lib/sekiban-moonbit`). None of these can work in a generated project --
the paths they reference simply don't exist there. Rather than let an npx
user hit a confusing "no such file or directory" if they read the source
sample's docs and try one of these flags, `sync-templates.mjs` injects a
small guard right after every bundled script's `cd "$ROOT"` line: if
`$ROOT/src/lib` is absent (true for every generated project) and one of
these flags/env vars is used, it prints a clear
`"<flag>' requires a full monorepo checkout ..."` message and exits 1
*before* the mode's own logic runs. The default (no flag/env var) path is
completely unaffected.

## Closeout learning

Verified locally with all five language toolchains present (cargo, node,
go, swift, moon):

- **Rust registry mode**: **fully build-verified** standalone, outside the
  monorepo -- `cargo check --workspace` inside the generated project
  genuinely compiles against the already-published crates.io crates and its
  bundled guard passes.
- **Rust dev mode**: **fully build-verified** standalone too --
  `cargo check --workspace` inside the generated project (Client + Wasm +
  6 vendored crates, no monorepo checkout) succeeds. This is the strongest
  evidence in this slice: a real local build against real vendored source,
  not just a passing static guard.
- **TypeScript, Swift, MoonBit registry mode**: their bundled
  `verify-no-local-sekiban-paths.sh` guards are static (grep-based, no live
  package resolution) and pass standalone regardless of publish status --
  **guard-verified**, though a full build still needs their respective
  publish batch to land.
- **Go registry mode**: its guard performs a live `go build`/`go vet`,
  which fails standalone today (`missing go.sum entry`) because
  `src/lib/sekiban-go@v0.1.0` is not published yet -- **tree-verified
  only**, expected until SWR-G061's tag publishes.
- **Standalone-mode guard**: verified directly against the generated
  TypeScript (`SEKIBAN_NPM_MODE=tarball`) and Go (`--local-module`) outputs
  -- both now fail with the clear guard message instead of a raw
  missing-path error; the default (registry) path is unaffected in both
  cases.

`--language all` generates all five languages correctly; unknown
`--language` values are rejected, and `--mode dev` for the four languages
without a dev-mode source sample is reported unavailable with a clear
message -- both produce no output, never broken output.

Whether pack-time bundling from monorepo samples "holds up" long-term: yes,
including the harder cases -- vendoring `wasm-projectors/rust` for Rust's
dev mode needed the workspace-package-inheritance fix above but was
otherwise mechanical, and the standalone-mode guard is a single,
uniformly-injected fix covering four different languages' pre-publish
flags. The one remaining gap is genuinely structural, not a shortcut: no
dev-mode sample exists yet in the monorepo for TypeScript, Go, Swift, or
MoonBit, so `create-sekiban-wasm` has nothing to bundle for their `dev`
mode. Closing that gap means adding a `PublicContainer.*` (or equivalent)
dev sample for each of those languages first -- a separate follow-up slice,
not something this generator can shortcut around.

## Availability and publish status

`create-sekiban-wasm` is not published to npm; this document and its
generation tests (`npm test`, `scripts/generate-smoke.sh`) are the
pre-publish verification path. No publish, token, or trusted-publishing
setup is part of this slice.
