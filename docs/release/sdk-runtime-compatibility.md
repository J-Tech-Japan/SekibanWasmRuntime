# SDK × Runtime Compatibility Matrix

Which SDK versions pair with which public runtime container image. All SDKs
speak the same serialized HTTP contract
(`/api/sekiban/serialized/tag-state|commit|query|list-query`) and, for
projector-side SDKs, the same guest ABI (alloc/dealloc string marshalling and
the materialized-view SQL statement protocol), so modules and clients built
with different SDKs interoperate on the same runtime image.

Runtime image: `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`.

The registry-verified current runtime-host tag is `1.0.0-preview.3`. <!-- release-lane: current-runtime-image-version -->

| SDK | Version | Distribution | Runtime image | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| Rust `sekiban-core` / `sekiban-derive` / `sekiban-wasm` / `sekiban-mv` / `sekiban-executor` | 0.1.0 | crates.io | `1.0.0-preview.3` | Published | crates.io consumer sample + public-container proof (SWR-G054..G056), `rust-crate-preview-readiness.md` |
| npm `@sekiban/ts` | 0.1.0 | npm via `ts-v*` lane (tag `ts-v0.1.0` + `npm-release` env pending, human-gated) | `1.0.0-preview.3` | Lane ready | Extraction smoke: packed-tarball client commit → tag-state → list-query readback (SWR-G057), `npm-ts-preview-readiness.md`; release lane with credential-free dry-run (SWR-G058), `npm-ts-release-lane.md` |
| npm `@sekiban/as-wasm` | 0.1.0 | npm via `ts-v*` lane (tag `ts-v0.1.0` + `npm-release` env pending, human-gated) | `1.0.0-preview.3` | Lane ready | Extraction smoke: packed-tarball projector module loaded and exercised in the public container (SWR-G057), `npm-ts-preview-readiness.md`; release lane with credential-free dry-run (SWR-G058), `npm-ts-release-lane.md` |
| npm `@sekiban/aspire` | 0.1.0 | npm via `ts-v*` lane (tag `ts-v0.1.0` + `npm-release` env pending, human-gated) | `1.0.0-preview.3` | Lane ready | TS AppHost smoke: packed-tarball helper started the public container via `aspire run` and passed health + commit/query round trip (SWR-G067), `public-container-ts-apphost.md` |
| Go `github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go` | 0.1.0 | Go subdirectory module (tag `src/lib/sekiban-go/v0.1.0` pending) | `1.0.0-preview.3` | Lane ready | Release-gate workflow (build/vet/test) + in-repo `go-wasm`/`go-clientapi` samples (SWR-G060), `go-sdk-release-lane.md`; public-container consumer proof follows in SWR-G061 |
| Swift `sekiban-swift` (products `SekibanWasm`, `SekibanMv`) | 0.1.0 (published, superseded) and 0.1.1 (published, clean) | Mirror repo `github.com/J-Tech-Japan/sekiban-swift`; both tags are immutable and consumers should select `v0.1.1` | `1.0.0-preview.3` | Published | SWR-G076 redirects SwiftPM scratch output outside the staged tree and asserts the exact published contents; the `v0.1.1` mirror tree contains exactly `LICENSE`, `Package.swift`, `README.md`, `Sources`, and `Tests`, while `v0.1.0` remains documented so publication history is not erased |
| MoonBit `sekiban/sekiban-wasm-runtime` + `sekiban/sekiban-client` | 0.1.0 | mooncakes.io (account/`sekiban` scope + tag `moonbit-v0.1.0` pending, human-gated) | `1.0.0-preview.3` | Lane prepared | Metadata gate + moon check/test + `moon package` dry-run producing the publish zips (SWR-G064), `moonbit-package-release-lane.md`; in-repo MoonBit sample pairs both packages; public-container consumer proof follows in SWR-G065 |

Framework baseline: all rows above sit on Sekiban.Dcb `10.16.0` and speak the V1
serialized commit envelope. The package-ground-truth finding and mixed-version
behavior are documented in
[`nuget-preview-readiness.md`](nuget-preview-readiness.md). The original
envelope introduction remains recorded in
[`sekiban-dcb-10.7.0-baseline.md`](sekiban-dcb-10.7.0-baseline.md).

Maintenance rule: when a new runtime image preview or a new SDK version ships,
add or update the row here in the same PR that ships it, citing the smoke or
consumer proof that established the pairing.
