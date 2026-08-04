// swift-tools-version: 6.0
import PackageDescription

// External-consumer proof: this sample depends on the public root package at
// an exact version — never a path-based local package reference
// (scripts/verify-no-local-sekiban-paths.sh guards this). Until the first
// Swift-consumable tag is cut, smoke.sh --local-package redirects this URL to
// an ephemeral local repository and never modifies this manifest.
//
// Linker flags mirror the in-repo Swift sample: the WASI reactor exec-model
// plus explicit --export entries per C-ABI symbol (Swift's LTO can strip
// @_cdecl functions unless they're listed).
let package = Package(
    name: "PublicSpmSwiftDecider",
    dependencies: [
        .package(name: "sekiban-swift", url: "https://github.com/J-Tech-Japan/SekibanWasmRuntime", exact: "1.0.0-preview.4"),
    ],
    targets: [
        .executableTarget(
            name: "PublicSpmSwiftDecider",
            dependencies: [
                .product(name: "SekibanWasm", package: "sekiban-swift"),
                .product(name: "SekibanMv", package: "sekiban-swift"),
            ],
            path: "Sources/PublicSpmSwiftDecider",
            linkerSettings: [
                .unsafeFlags([
                    "-Xclang-linker", "-mexec-model=reactor",
                    "-Xlinker", "--import-undefined",
                    "-Xlinker", "--export=alloc",
                    "-Xlinker", "--export=dealloc",
                    "-Xlinker", "--export=create_instance",
                    "-Xlinker", "--export=apply_event",
                    "-Xlinker", "--export=apply_event_with_metadata",
                    "-Xlinker", "--export=apply_events_batch",
                    "-Xlinker", "--export=serialize_state",
                    "-Xlinker", "--export=restore_state",
                    "-Xlinker", "--export=execute_query",
                    "-Xlinker", "--export=execute_list_query",
                    "-Xlinker", "--export=mv_metadata",
                    "-Xlinker", "--export=mv_initialize",
                    "-Xlinker", "--export=mv_apply_event",
                ]),
            ]),
    ]
)
