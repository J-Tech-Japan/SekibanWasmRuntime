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

- **`registry` mode** (the only mode implemented in 0.1.0) generates an
  external-consumer sample: the project depends on the published Sekiban
  package for that language only, at an exact version, with **no** local
  path or workspace-relative dependency of any kind. Every registry-mode
  template ships the same `scripts/verify-no-local-sekiban-paths.sh` guard
  its monorepo source sample has, adapted to run standalone (see
  [Portability adjustments](#portability-adjustments-at-pack-time)). The
  generated `README.md` states the mode at the top.
- **`dev` mode** (local build against the public runtime container from
  repository-local source, mirroring samples like
  `Sekiban.Dcb.WasmRuntime.PublicContainer.RsDecider`) is **not bundled in
  0.1.0**. Passing `--mode dev` reports a clear "not available" message and
  generates nothing -- it never produces broken output. The reason: the
  monorepo's local-container dev samples depend on internal library crates
  (for Rust, everything under `src/wasm-projectors/rust`) that are not
  vendored into this package; bundling them would either duplicate a
  moving-target internal SDK inside a public npm tarball or require a
  template-sync mechanism beyond this slice's scope. This is tracked as a
  follow-up, not a silent gap -- see
  [Closeout learning](#closeout-learning).

## Per-language availability (0.1.0)

| Language | Registry mode | Dev mode |
| --- | --- | --- |
| Rust | Available -- depends on crates.io `sekiban-*` at exact `=0.1.0`. **Already published**; the generated project builds and runs standalone today. | Not available |
| TypeScript | Available -- depends on `@sekiban/ts`/`@sekiban/as-wasm` at exact npm `0.1.0`. Not published yet (SWR-G058); `npm install` 404s until it is. | Not available |
| Go | Available -- requires the published module `github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go` at `v0.1.0`. Tag not published yet; `go build` fails to resolve it until it is. | Not available |
| Swift | Available -- depends on the public `github.com/J-Tech-Japan/sekiban-swift` mirror at exact `0.1.0`. Mirror visibility is a separate human-gated step. | Not available |
| MoonBit | Available -- depends on `sekiban/sekiban-wasm-runtime` and `sekiban/sekiban-client` as mooncakes.io registry packages. Not published yet. | Not available |

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
`sync-templates.mjs` self-checks its own output for `src/samples/` or
`../../../..` residue and fails loudly if any adjustment was missed.

## Closeout learning

Verified locally with all five language toolchains present (cargo, node,
go, swift, moon):

- **Rust**: registry-mode output is **fully build-verified** standalone,
  outside the monorepo -- `cargo check --workspace` inside the generated
  project genuinely compiles against the already-published crates.io
  crates and its bundled guard passes.
- **TypeScript, Swift, MoonBit**: their bundled
  `verify-no-local-sekiban-paths.sh` guards are static (grep-based, no live
  package resolution) and pass standalone regardless of publish status --
  **guard-verified**, though a full build still needs their respective
  publish batch to land.
- **Go**: its guard performs a live `go build`/`go vet`, which fails
  standalone today (`missing go.sum entry`) because
  `src/lib/sekiban-go@v0.1.0` is not published yet -- **tree-verified
  only**, expected until SWR-G061's tag publishes.

`--language all` generates all five languages correctly; unknown
`--language` values and `--mode dev` are both rejected with clear messages
and produce no output, never broken output.

Whether pack-time bundling from monorepo samples "holds up" long-term: yes
for the mechanical parts (directory copy + path-portability rewrites are
simple, self-checked, and did not need special-casing beyond what's
documented above), but the **dev-mode gap is a real, non-cosmetic
limitation** worth a dedicated follow-up decision: either vendor the
relevant internal library crates/packages per language at pack time, or
restructure the `PublicContainer.*` dev samples to depend on their
published packages the same way the registry samples already do (removing
the need to vendor anything). Either path is future work, not part of this
slice.

## Availability and publish status

`create-sekiban-wasm` is not published to npm; this document and its
generation tests (`npm test`, `scripts/generate-smoke.sh`) are the
pre-publish verification path. No publish, token, or trusted-publishing
setup is part of this slice.
