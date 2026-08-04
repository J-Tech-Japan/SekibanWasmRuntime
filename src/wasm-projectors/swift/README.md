# sekiban-swift

Swift SDK for the [Sekiban WASM Runtime](https://github.com/J-Tech-Japan/SekibanWasmRuntime):
build Sekiban DCB projector modules in Swift and compile them to WebAssembly
(WASI) for the public runtime container.

This is **one SPM package with two products**. Its `Package.swift` lives at
the [SekibanWasmRuntime repository root](https://github.com/J-Tech-Japan/SekibanWasmRuntime),
which is required for SwiftPM Git resolution; sources stay in this directory.

```swift
// Package.swift of your projector module
dependencies: [
    .package(name: "sekiban-swift", url: "https://github.com/J-Tech-Japan/SekibanWasmRuntime", exact: "1.0.0-preview.4"),
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

The Swift SDK uses this repository's release version. `v1.0.0-preview.4` is
the first SwiftPM-resolvable release because the earlier preview tags predate
the root manifest. It targets the public runtime container image of the same
version and implements the same guest ABI as the Rust `sekiban-wasm`/`sekiban-mv` 0.1.0 crates, the
npm `@sekiban/as-wasm` 0.1.0 package, and the Go SDK — modules built with any
of these SDKs run side by side on the same runtime image.

## License

[Elastic License 2.0](./LICENSE)
