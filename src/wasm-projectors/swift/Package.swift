// swift-tools-version: 6.0
import PackageDescription

// Sekiban Swift SDK — one SPM package exposing both SDK layers as separate
// targets/products. This exact manifest is mirrored to the repository root of
// github.com/J-Tech-Japan/sekiban-swift (SwiftPM resolves packages from a
// repository root only), so target, product, and import names below are public
// API fixed before the first publish:
//
//   * `SekibanWasm` — FFI plumbing for Sekiban WASM projector modules
//     (read/write UTF-8 strings in linear memory, pack/unpack pointer+length
//     into a single i64, alloc/dealloc exports) plus the primitive projection
//     C-ABI export stubs so an MV-only module still satisfies the host's
//     export probe.
//   * `SekibanMv` — Swift companion to the host-side MV wire contracts
//     (`WasmMvBoundaryContracts`): Codable DTOs, the `WasmMvProjector`
//     protocol, a registry, `MvParamBuilder`, and the `mv_metadata` /
//     `mv_initialize` / `mv_apply_event` C-ABI exports generated from the
//     registry contents. Mirrors the responsibilities of the Rust `sekiban-mv`
//     crate.
let package = Package(
    name: "sekiban-swift",
    products: [
        .library(name: "SekibanWasm", type: .static, targets: ["SekibanWasm"]),
        .library(name: "SekibanMv", type: .static, targets: ["SekibanMv"]),
    ],
    targets: [
        .target(name: "SekibanWasm", path: "Sources/SekibanWasm"),
        .target(
            name: "SekibanMv",
            dependencies: ["SekibanWasm"],
            path: "Sources/SekibanMv",
            // `@_extern(c, "...")` marks an undefined Swift declaration as a plain C-ABI WASM
            // import. Without it, `@_silgen_name` keeps Swift's native calling convention and
            // wasm-ld emits a 7-param import signature the host cannot satisfy.
            swiftSettings: [
                .enableExperimentalFeature("Extern"),
                .unsafeFlags(["-Xfrontend", "-enable-experimental-feature",
                              "-Xfrontend", "Extern"]),
            ]),
        .testTarget(
            name: "SekibanSwiftTests",
            dependencies: ["SekibanWasm", "SekibanMv"],
            path: "Tests/SekibanSwiftTests"),
    ]
)
