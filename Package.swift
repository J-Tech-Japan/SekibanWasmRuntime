// swift-tools-version: 6.0
import PackageDescription

// Sekiban Swift SDK — one root-level SPM package exposing both SDK layers as
// separate products. SwiftPM resolves a Git dependency from the repository
// root, while the implementation remains under src/wasm-projectors/swift.
let package = Package(
    name: "sekiban-swift",
    products: [
        .library(name: "SekibanWasm", type: .static, targets: ["SekibanWasm"]),
        .library(name: "SekibanMv", type: .static, targets: ["SekibanMv"]),
    ],
    targets: [
        .target(name: "SekibanWasm", path: "src/wasm-projectors/swift/Sources/SekibanWasm"),
        .target(
            name: "SekibanMv",
            dependencies: ["SekibanWasm"],
            path: "src/wasm-projectors/swift/Sources/SekibanMv",
            // @_extern(c, "...") requires the Extern experimental feature so
            // Swift emits a plain C-ABI WebAssembly import.
            swiftSettings: [
                .enableExperimentalFeature("Extern"),
            ]),
        .testTarget(
            name: "SekibanSwiftTests",
            dependencies: ["SekibanWasm", "SekibanMv"],
            path: "src/wasm-projectors/swift/Tests/SekibanSwiftTests"),
    ])
