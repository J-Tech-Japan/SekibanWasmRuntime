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
  protected environment; runs the sync script in `--push` mode only after the
  required reviewer approval.

## First-publish prerequisites (provisioned)

The mirror repository, push token, and protected `swift-mirror-release`
environment are provisioned. Before the post-merge `swift-v0.1.0` re-cut,
the operator must re-prove that the mirror is empty and has zero remote tags.
The operator/reviewer then approves the protected environment; implementation
work must not approve or publish.

After the first successful push, verify consumer resolution from a scratch
package:

```bash
cd "$(mktemp -d)" && swift package init --type executable
# add to Package.swift:
#   .package(url: "https://github.com/J-Tech-Japan/sekiban-swift", from: "0.1.0")
swift package resolve
```

## Consumer Proof (SWR-G063)

[`src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider`](../../src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider)
is the external-consumer proof for this lane: its committed `Package.swift`
depends on `https://github.com/J-Tech-Japan/sekiban-swift` at exact 0.1.0 with
no path-based references (guard: `scripts/verify-no-local-sekiban-paths.sh`),
and its smoke validates command execution, tag-state readback, in-memory
projection queries, and materialized-view catch-up against the public GHCR
runtime container.

Two-stage verification:

- **Pre-publish dry-run** (demonstrated now, NOT release evidence):
  `smoke.sh --local-package` stages the mirror tree with this lane's sync
  dry-run, tags it `v0.1.0` in a local git repo, and redirects the dependency
  via SwiftPM dependency mirroring — the committed manifest is untouched.
- **Mirror-resolved run** (release evidence, after the mirror is public at
  v0.1.0): `smoke.sh` resolves the dependency from the real mirror URL; this
  is the recorded follow-up once the human mirror-publish batch completes.

Consumer-surfaced constraint (fixed in SWR-G063): SwiftPM **rejects versioned
remote dependencies whose targets declare `unsafeFlags`**, so the `SekibanMv`
target now uses only the safe `.enableExperimentalFeature("Extern")` setting —
an `unsafeFlags` entry would have made the published package unconsumable.

## Linux Container Build (SWR-G063)

`src/samples/Sekiban.Dcb.WasmRuntime.PublicSpm.SwiftDecider/scripts/linux-build-check.sh`
stages the mirror tree and runs `swift build` plus a time-bounded `swift test`
inside a `swift:6.1-noble` Linux container. Outcome (2026-07-03, aarch64):
**works with caveats** — `swift build` succeeds against the staged mirror
tree, but the XCTest runner hangs at test execution (observed both unbounded
for hours and within the bounded window; the check records this instead of
hanging). Library consumption is build-time only for wasm projector modules,
so Linux consumers can build against the package today; XCTest-runner
remediation is follow-up work per the SWR-G063 scope. Evidence:
`reports/smoke/sekiban-swift-linux-build.md` (regenerated per run).

## Compatibility

`sekiban-swift` 0.1.x pairs with runtime image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and implements
the same guest ABI as the Rust `sekiban-wasm`/`sekiban-mv` 0.1.0 crates — see
`sdk-runtime-compatibility.md`. The Swift external-consumer sample against the
public container is SWR-G063 (above).

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

## CI shell compatibility and SWR-G075 handoff

GitHub Actions defaults run steps in a container job to `sh -e {0}`. On the
failed run, that meant dash executed `set -o pipefail` even though the
unmodified `swift:6.1-noble` image contains `/usr/bin/bash` GNU bash 5.2.21.
Every inline workflow step therefore declares `shell: sh` and uses POSIX
syntax; the existing sync script continues to run deliberately via `bash`.
Strict tag validation lives in `scripts/release/validate-swift-tag.sh`, and
the committed `test-validate-swift-tag.sh` matrix runs from the CI gate.

An audit of all `release-*.yml` workflows found no other workflow combining a
job-level `container:` with inline bash-only syntax; the other release lanes
run on their normal hosted Ubuntu shell. The Swift lane's explicit shell is the
remaining container compatibility boundary.

The failed first run (30876914701) never reached the approval gate. After this
fix merges to `main`, the operator must first re-prove that
`J-Tech-Japan/sekiban-swift` is empty with zero remote tags, then re-cut
`swift-v0.1.0` at the merged commit. Re-run the gate and confirm
`mirror-push` is waiting on the `swift-mirror-release` approval gate. Do not
approve the gate or publish from implementation work.

### Outcome and the design lesson (SWR-G075 closeout)

That sequence was carried out and **Swift SDK 0.1.0 is published**: the re-cut
tag ran green, the operator approved `swift-mirror-release`, and the mirror
received the package at tag `v0.1.0` (`d73b4767`). Publication was proven by
consumption, not by a green workflow — a throwaway package declaring
`.package(url: "https://github.com/J-Tech-Japan/sekiban-swift", exact: "0.1.0")`
resolved and built, linking `libSekibanWasm.a`.

Two lessons are worth keeping, because neither was visible from inside the
implementation:

**A lane that has never run is not a lane.** This one was authored, reviewed,
merged, and documented as ready long before it could execute, because it was
blocked on human prerequisites (mirror repository, token, protected
environment). The first real tag push was also its first execution, and it
failed on its first line. A dry-run passing locally proved nothing about CI: it
ran under the developer's shell, not the container's. Treat "prepared" and
"proven runnable" as different states, and prefer proving a lane end to end —
even against a throwaway version — over declaring it ready.

**Diagnose before recording a cause.** The first root cause recorded for this
failure was that the image lacked bash. It was wrong, asserted without probing
the image, and it would have been preserved here as documentation had review not
disproved it with `docker run --rm swift:6.1-noble sh -c 'command -v bash'`. The
true cause — Actions defaulting container-job steps to `sh` — is the one written
above. A plausible cause recorded as fact is worse than an open question,
because it stops the next person from looking.

