# Public Packages

Everything SekibanWasmRuntime publishes, in one place: what is consumable
today, how to depend on it, and what is still waiting.

Statuses mean exactly one thing here:

- **Published** — resolvable from the public registry right now, verified by
  actually consuming it.
- **Lane ready** — the code, the release workflow, and its dry-run all pass, but
  nothing is published because the registry credential or account is still an
  operator action.

Two facts shape the version numbers below. The **runtime container** and the
**.NET packages** use the same `1.0.0-preview.N` version syntax, but they are
independent release lanes and their current versions do not have to match.
Every **language SDK** carries its own line starting at `0.1.0`, because each
ships on its own lane and moves at its own pace. So an SDK version never has to
match a runtime version — see
[`release/sdk-runtime-compatibility.md`](release/sdk-runtime-compatibility.md)
for which pairs are proven together.

The current-state versus historical-evidence rule is defined in
[`release/consumer-version-policy.md`](release/consumer-version-policy.md).

---

## Published

### Runtime container — the thing everything else talks to

```
ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3
ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:preview
```

The registry-verified current runtime-host tag is `1.0.0-preview.3`. <!-- release-lane: current-runtime-image-version -->

The serialized HTTP runtime that loads your WASM projector and serves
`/api/sekiban/serialized/{tag-state,commit,query,list-query}`. Bring your own
Postgres; mount your manifest and `.wasm`. Start here — every SDK below is a
client or a projector for this image. See
[`release/ghcr-image-preview.md`](release/ghcr-image-preview.md).

### Registry evidence for the runtime-host discrepancy

The registry is the source of truth for a consumer image. On 2026-08-14,
`docker buildx imagetools inspect` reported:

- `:1.0.0-preview.3` — OCI index digest
  `sha256:8bdebccdd81d02bc958bcf422eea5ffbafd3f2cc2eec5fe97c4b7129a16db79f`,
  with `linux/amd64` and `linux/arm64` manifests.
- `:preview` — the same digest and both platforms.
- `:1.0.0-preview.5` — not found.

The repository tag history currently exposes only
`runtime-host-v1.0.0-preview.1`, whose registry digest is the older amd64-only
`sha256:5b94ca79f10507aaee6ff3652e45451ea3b3ff47c55d05465a4424ec3be45e59`.
Therefore the stale side of the discrepancy was the source tag-lane
assumption, not the consumer-facing `.3` tag. The `.3` image reference above is
retained and is the value checked by the release-lane marker.

### .NET — NuGet

```xml
<PackageReference Include="Sekiban.Dcb.WasmRuntime" Version="1.0.0-preview.5" />
<PackageReference Include="Sekiban.Dcb.WasmRuntime.Remote" Version="1.0.0-preview.5" />
<PackageReference Include="Sekiban.Dcb.WasmRuntime.Aspire" Version="1.0.0-preview.5" />
```

The current published NuGet package line is `1.0.0-preview.5`. <!-- release-lane: current-package-version -->
The current Sekiban.Dcb baseline is `10.16.0`. <!-- release-lane: current-dcb-version -->

| Package | What it is |
| --- | --- |
| `Sekiban.Dcb.WasmRuntime` | Core runtime abstractions |
| `Sekiban.Dcb.WasmRuntime.Remote` | `RemoteSekibanExecutor` — typed client over the serialized HTTP contract |
| `Sekiban.Dcb.WasmRuntime.Aspire` | `AddSekibanWasmRuntime(name, opts)` for a C# Aspire AppHost |

All three sit on Sekiban.Dcb `10.16.0`. `Sekiban.Cloud.Client 1.0.0-preview.1` is
also on NuGet but predates that baseline.

### Rust — crates.io

```toml
[dependencies]
sekiban-core = "0.1.0"
sekiban-derive = "0.1.0"
sekiban-wasm = "0.1.0"      # projector-side
sekiban-mv = "0.1.0"        # materialized views
sekiban-executor = "0.1.0"  # client-side
```

### Swift — SwiftPM

```swift
.package(url: "https://github.com/J-Tech-Japan/sekiban-swift", from: "0.1.1"),
// products: SekibanWasm, SekibanMv
```

Published through the mirror repository
[`J-Tech-Japan/sekiban-swift`](https://github.com/J-Tech-Japan/sekiban-swift),
synced from `src/wasm-projectors/swift` at release time. The mirror exists
because SwiftPM resolves a git dependency only from a `Package.swift` at the
**repository root** and offers no subdirectory selector; keeping the manifest at
this repository's root would have forced the Swift SDK onto the repository
release line and destroyed its independent versioning. That trade-off was
decided deliberately, and reversing it was considered and rejected.

**Use `0.1.1`, not `0.1.0`.** `0.1.0` resolves and builds correctly, but its
tree carries about 47.5 MiB across 963 `.build` entries that were published by
mistake. `0.1.1` is the same SDK with a clean tree. `0.1.0` is left in place
because published versions are immutable.

---

## Lane ready — waiting on an operator action

These are not "unfinished". Each has a consumer sample, a public-container
end-to-end smoke, a no-local-path guard, and a release workflow whose dry-run
passes. What is missing is a registry credential or account, which only a human
can provision.

| Artifact | Registry | Blocked on |
| --- | --- | --- |
| `@sekiban/ts`, `@sekiban/as-wasm`, `@sekiban/aspire` | npm | `@sekiban` scope auth + `npm-release` environment approval |
| `sekiban/sekiban-wasm-runtime`, `sekiban/sekiban-client` | mooncakes.io | account + `sekiban` scope + `moon publish` auth |
| `Sekiban.Dcb.WasmRuntime.Templates` (`dotnet new sekiban-wasm-decider`) | NuGet | Trusted Publishing policy for the new package id |

`create-sekiban-wasm` is not lane ready yet: it has no publishing workflow.
The `npx create-sekiban-wasm` command becomes a public install path only after
that separate lane is implemented and published.

**Go is a special case: nothing is blocked, only untagged.** It publishes as a
monorepo subdirectory module — no mirror, no credential, because the repository
is already public and Go modules *do* have a subdirectory selector:

```go
import "github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go"
```

Cutting the tag `src/lib/sekiban-go/v0.1.0` publishes it. The prefixed tag form
is required by the Go toolchain, and it is exactly what lets Go keep an
independent version line without a separate repository — the option SwiftPM does
not offer.

---

## Release lanes

Each artifact family has its own tag prefix so lanes never collide. Full table
in [`release/release-tag-conventions.md`](release/release-tag-conventions.md).

| Lane | Tag | Trigger |
| --- | --- | --- |
| NuGet packages | `v<version>` | GitHub Release |
| Templates package | `templates-v<version>` | GitHub Release |
| Rust crates | `rust-v<version>` | GitHub Release |
| npm `@sekiban/*` | `ts-v<version>` | GitHub Release |
| Runtime host image | `runtime-host-v<version>` | tag push |
| Swift SPM | `swift-v<version>` (mirror gets plain `v<version>`) | tag push |
| MoonBit | `moonbit-v<version>` | tag push |
| Go SDK | `src/lib/sekiban-go/v<version>` | tag push |

Publishing controls vary by lane. The Swift mirror uses the protected
`swift-mirror-release` environment with required reviewers. Other workflows
may name an environment without required-reviewer rules, and the runtime image
lane publishes directly from its tag/manual trigger without an environment.
Check the target workflow and current GitHub environment rules before cutting a
release; an environment name alone does not guarantee a human approval pause.

---

## Choosing what to depend on

- **Running the runtime yourself** → the GHCR image, plus the SDK for whatever
  language your projector is written in.
- **Talking to a runtime from .NET** → `Sekiban.Dcb.WasmRuntime.Remote`.
- **Wiring it into .NET Aspire** → `Sekiban.Dcb.WasmRuntime.Aspire`, or
  `dotnet new sekiban-wasm-decider` once the template package ships.
- **Writing a projector** → the projector-side SDK for your language
  (`sekiban-wasm` in Rust, `SekibanWasm` in Swift, `@sekiban/as-wasm` in
  AssemblyScript, and so on).
- **Just trying it** → [`quickstart.md`](quickstart.md), or `npx
  create-sekiban-wasm` once that lane publishes.

## Where to look next

- [`release/sdk-runtime-compatibility.md`](release/sdk-runtime-compatibility.md)
  — which SDK version is proven against which runtime image, with the evidence.
- [`release/release-tag-conventions.md`](release/release-tag-conventions.md) —
  every tag prefix and what it triggers.
- `release/*-release-lane.md` — the per-lane mechanics, including what a lane
  refuses to do without an operator.
