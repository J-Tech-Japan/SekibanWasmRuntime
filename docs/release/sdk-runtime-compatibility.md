# SDK × Runtime Compatibility Matrix

Which SDK versions pair with which public runtime container image. All SDKs
speak the same serialized HTTP contract
(`/api/sekiban/serialized/tag-state|commit|query|list-query`) and, for
projector-side SDKs, the same guest ABI (alloc/dealloc string marshalling and
the materialized-view SQL statement protocol), so modules and clients built
with different SDKs interoperate on the same runtime image.

Runtime image: `ghcr.io/j-tech-japan/sekiban-wasm-runtime-host`.

| SDK | Version | Distribution | Runtime image | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| Rust `sekiban-core` / `sekiban-derive` / `sekiban-wasm` / `sekiban-mv` / `sekiban-executor` | 0.1.0 | crates.io | `1.0.0-preview.3` | Published | crates.io consumer sample + public-container proof (SWR-G054..G056), `rust-crate-preview-readiness.md` |
| npm `@sekiban/ts` | 0.1.0 | npm (publish pending, human-gated batch) | `1.0.0-preview.3` | Pack-ready | Extraction smoke: packed-tarball client commit → tag-state → list-query readback (SWR-G057), `npm-ts-preview-readiness.md` |
| npm `@sekiban/as-wasm` | 0.1.0 | npm (publish pending, human-gated batch) | `1.0.0-preview.3` | Pack-ready | Extraction smoke: packed-tarball projector module loaded and exercised in the public container (SWR-G057), `npm-ts-preview-readiness.md` |
| Go `github.com/J-Tech-Japan/SekibanWasmRuntime/src/lib/sekiban-go` | 0.1.0 | Go subdirectory module (tag `src/lib/sekiban-go/v0.1.0` pending) | `1.0.0-preview.3` | Lane ready | Release-gate workflow (build/vet/test) + in-repo `go-wasm`/`go-clientapi` samples (SWR-G060), `go-sdk-release-lane.md`; public-container consumer proof follows in SWR-G061 |

Maintenance rule: when a new runtime image preview or a new SDK version ships,
add or update the row here in the same PR that ships it, citing the smoke or
consumer proof that established the pairing.
