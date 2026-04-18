// swift-tools-version: 6.0
import PackageDescription

// Swift companion to the host-side MV wire contracts declared in
// `Sekiban.Dcb.WasmRuntime.Host.MaterializedView.WasmMvBoundaryContracts`. Provides
// Codable DTOs, a `WasmMvProjector` protocol, a registry, a `MvParamBuilder`, and the
// `mv_metadata` / `mv_initialize` / `mv_apply_event` C-ABI exports generated from the
// registry contents. Mirrors the responsibilities of the Rust `sekiban-mv` crate.
let package = Package(
    name: "sekiban-mv",
    products: [
        .library(name: "SekibanMv", type: .static, targets: ["SekibanMv"]),
    ],
    dependencies: [
        .package(path: "../sekiban-wasm"),
    ],
    targets: [
        .target(
            name: "SekibanMv",
            dependencies: [
                .product(name: "SekibanWasm", package: "sekiban-wasm"),
            ],
            path: "Sources/SekibanMv",
            // `@_extern(c, "...")` marks an undefined Swift declaration as a plain C-ABI WASM
            // import. Without it, `@_silgen_name` keeps Swift's native calling convention and
            // wasm-ld emits a 7-param import signature the host cannot satisfy.
            swiftSettings: [
                .enableExperimentalFeature("Extern"),
                .unsafeFlags(["-Xfrontend", "-enable-experimental-feature",
                              "-Xfrontend", "Extern"]),
            ]),
    ]
)
