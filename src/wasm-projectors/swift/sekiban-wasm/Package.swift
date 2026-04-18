// swift-tools-version: 6.0
import PackageDescription

// Shared Swift helper package for Sekiban WASM modules. Provides FFI plumbing (read/write
// UTF-8 strings in linear memory, pack/unpack pointer+length into a single i64, alloc/dealloc
// exports) and the stubs for primitive projection C-ABI exports so a MV-only module still
// satisfies the host's export probe.
let package = Package(
    name: "sekiban-wasm",
    products: [
        .library(name: "SekibanWasm", type: .static, targets: ["SekibanWasm"]),
    ],
    targets: [
        .target(name: "SekibanWasm", path: "Sources/SekibanWasm"),
    ]
)
