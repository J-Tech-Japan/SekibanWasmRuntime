# Swift SDK Release Lane (SWR-G062)

The Swift SDK at `src/wasm-projectors/swift` is **one SPM package with two
products** and publishes through the mirror repository
`github.com/J-Tech-Japan/sekiban-swift`. SwiftPM can only resolve a package
whose `Package.swift` sits at a repository root, so — unlike Go's subdirectory
modules — the Swift SDK cannot be consumed from the monorepo directly. The
monorepo stays the source of truth; the mirror is write-only, updated by the
sync script below, and never edited directly.

## Package Shape and Naming (fixed before first publish)

Decided in this slice and **fixed as public API from the first publish on**:

| Surface | Name |
| --- | --- |
| Package name (manifest `name:`) | `sekiban-swift` |
| Mirror repository | `github.com/J-Tech-Japan/sekiban-swift` |
| Products (both `.static` libraries) | `SekibanWasm`, `SekibanMv` |
| Targets | `SekibanWasm`, `SekibanMv` |
| Import statements | `import SekibanWasm`, `import SekibanMv` |

Consumers reference products as
`.product(name: "SekibanWasm", package: "sekiban-swift")` /
`.product(name: "SekibanMv", package: "sekiban-swift")`. The former standalone
packages `sekiban-wasm` and `sekiban-mv` were consolidated into this single
package; target/product/import names were deliberately kept so consumer source
code did not change — only `Package.swift` dependency declarations moved to the
one package. In-repo consumers use
`.package(name: "sekiban-swift", path: …)` (the explicit `name:` is needed
because SwiftPM derives a local path dependency's identity from the directory
basename, which is `swift` in the monorepo).

## Tag Convention

- Monorepo release tag: `swift-vX.Y.Z` (first release `swift-v0.1.0`).
- Mirror repository tag: plain `vX.Y.Z` (what SwiftPM consumers see as
  `from: "0.1.0"`); created by the sync script during `--push`.

## Sync Flow

`scripts/release/sync-sekiban-swift-mirror.sh`:

1. **Stage** — copies exactly `Package.swift`, `README.md`, `LICENSE`,
   `Sources/`, `Tests/` from `src/wasm-projectors/swift` into
   `artifacts/sekiban-swift-mirror/tree` (the exact mirror-root layout).
2. **Guard** — fails if the staged manifest declares `.package(path:)`
   dependencies, references parent directories (`../`), or mentions monorepo
   paths: the mirror tree must be fully self-contained.
3. **Validate** — runs `swift build` and `swift test` inside the staged tree,
   exactly as an external consumer would receive it.
4. **Push** (`--push --version X.Y.Z`, blocked — see below) — clones the
   mirror, replaces its content with the staged tree, commits with the source
   monorepo commit in the message, pushes, and tags `vX.Y.Z`.

`--dry-run` performs steps 1–3 only and needs no mirror repository, network
access to it, or token.

## Release Workflow

`.github/workflows/release-swift-sdk.yml` triggers on `swift-v*` tags plus
`workflow_dispatch` (pre-tag dry run):

- **gate** (always): tag-format check (`swift-vX.Y.Z`), `swift build`,
  `swift test`, and the dry-run sync including the path-leakage guard, inside
  a `swift:6.1-noble` container.
- **mirror-push** (tags only): gated behind the `swift-mirror-release`
  protected environment; runs the sync script in `--push` mode. **Currently
  blocked on human prerequisites** — until they are done, the step fails fast
  with an explicit message and nothing is published.

## Human Prerequisites (blocking the first publish)

1. Create the mirror repository `github.com/J-Tech-Japan/sekiban-swift`
   (public, empty; no default README so the first sync is clean).
2. Provision a push token for it and store it as the
   `SEKIBAN_SWIFT_MIRROR_TOKEN` secret scoped to the `swift-mirror-release`
   protected environment.
3. Create/approve the `swift-mirror-release` protected environment with the
   required reviewers.
4. Push the `swift-v0.1.0` tag on the accepted commit; approve the
   environment gate.

After the first successful push, verify consumer resolution from a scratch
package:

```bash
cd "$(mktemp -d)" && swift package init --type executable
# add to Package.swift:
#   .package(url: "https://github.com/J-Tech-Japan/sekiban-swift", from: "0.1.0")
swift package resolve
```

## Compatibility

`sekiban-swift` 0.1.x pairs with runtime image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and implements
the same guest ABI as the Rust `sekiban-wasm`/`sekiban-mv` 0.1.0 crates — see
`sdk-runtime-compatibility.md`. The Swift external-consumer sample against the
public container is SWR-G063.

## Consolidation Notes (closeout learning)

- The two former packages moved from
  `src/wasm-projectors/swift/{sekiban-wasm,sekiban-mv}/Sources/*` to
  `src/wasm-projectors/swift/Sources/{SekibanWasm,SekibanMv}` under one root
  manifest; no source-level API changed.
- One code change was forced by consolidation: adding a test target links the
  SDK into a native test binary, which surfaced the `mv_host_query_rows` wasm
  host import as an undefined native symbol. The import declaration in
  `SekibanMv/QueryPort.swift` is now `#if arch(wasm32)`-guarded with a native
  stub returning "no result"; wasm builds are byte-for-byte equivalent
  (verified via `build/scripts/build-swift-wasm.sh` with the full C-ABI export
  list intact).
