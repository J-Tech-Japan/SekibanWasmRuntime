# sekiban-swift

Swift SDK for the [Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime):
build Sekiban DCB projector modules in Swift and compile them to WebAssembly
(WASI) for the public runtime container.

This is **one SPM package with two products**. SwiftPM resolves packages from a
repository root, so the package is published through the mirror repository
`github.com/J-Tech-Japan/sekiban-swift`, whose content is synced from the
[Swift SDK directory of the SekibanWasmRuntime monorepo](https://github.com/J-Tech-Japan/SekibanWasmRuntime/tree/main/src/wasm-projectors/swift)
(the monorepo is the source of truth; do not commit to the mirror directly).

```swift
// Package.swift of your projector module
dependencies: [
    .package(url: "https://github.com/J-Tech-Japan/sekiban-swift", from: "0.1.0"),
],
targets: [
    .executableTarget(
        name: "MyProjector",
        dependencies: [
            .product(name: "SekibanWasm", package: "sekiban-swift"),
            .product(name: "SekibanMv", package: "sekiban-swift"),
        ]),
]
```

## Products

- **`SekibanWasm`** (`import SekibanWasm`) — FFI plumbing for projector
  modules: read/write UTF-8 strings in linear memory, pack/unpack
  pointer+length into a single `i64` (`packPtrLen`/`unpackPtrLen`),
  `alloc`/`dealloc` exports, JSON/error envelope writers, and the primitive
  projection C-ABI export stubs so an MV-only module still satisfies the
  host's export probe.
- **`SekibanMv`** (`import SekibanMv`) — Swift companion to the host-side
  materialized-view wire contracts: Codable DTOs (`MvParam`,
  `MvSqlStatementDto`, …), the `WasmMvProjector` protocol and registry,
  `MvParamBuilder`, the `mv_metadata` / `mv_initialize` / `mv_apply_event`
  C-ABI exports, and a host-backed query port. Mirrors the responsibilities of
  the Rust `sekiban-mv` crate.

Target, product, and import names are public API, fixed before the first
publish — see the
[Swift SDK release lane doc](https://github.com/J-Tech-Japan/SekibanWasmRuntime/blob/main/docs/release/swift-sdk-release-lane.md).

## Building a module

Compile with a Swift WebAssembly SDK (swift-tools 6.0+, WASI reactor model):

```bash
swift build --swift-sdk <your-wasm-sdk> -c release
```

A complete projector built on this package lives in the monorepo:
[Sekiban.Dcb.Orleans.Decider.Wasm.Swift sample](https://github.com/J-Tech-Japan/SekibanWasmRuntime/tree/main/src/samples/Sekiban.Dcb.Orleans.Decider.Wasm.Swift)
(its [build script](https://github.com/J-Tech-Japan/SekibanWasmRuntime/blob/main/build/scripts/build-swift-wasm.sh)
shows the exact toolchain invocation and the linker flags required for the
reactor exec-model and C-ABI export list).

## Runtime pairing

`sekiban-swift` 0.1.x targets the public runtime container image
`ghcr.io/j-tech-japan/sekiban-wasm-runtime-host:1.0.0-preview.3` and implements
the same guest ABI as the Rust `sekiban-wasm`/`sekiban-mv` 0.1.0 crates, the
npm `@sekiban/as-wasm` 0.1.0 package, and the Go SDK — modules built with any
of these SDKs run side by side on the same runtime image.

## License

[Elastic License 2.0](./LICENSE)
