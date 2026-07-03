// swift-tools-version: 6.0
import PackageDescription

// External-consumer proof: this sample depends on the PUBLIC sekiban-swift
// mirror repository at an exact version — never a path-based local package
// reference (scripts/verify-no-local-sekiban-paths.sh guards this). Pre-publish
// dry-runs redirect the URL to a locally staged mirror tree via SwiftPM's
// dependency-mirroring configuration (scripts/smoke.sh --local-package), which
// never modifies this manifest.
//
// Linker flags mirror the in-repo Swift sample: the WASI reactor exec-model
// plus explicit --export entries per C-ABI symbol (Swift's LTO can strip
// @_cdecl functions unless they're listed).
let package = Package(
    name: "PublicSpmSwiftDecider",
    dependencies: [
        .package(url: "https://github.com/J-Tech-Japan/sekiban-swift", exact: "0.1.0"),
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
