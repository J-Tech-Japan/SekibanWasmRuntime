// swift-tools-version: 6.0
import PackageDescription

// Single-binary Swift WASM module for the Sekiban DCB Swift sample. Combines the shared
// primitive-ABI stubs (SekibanWasm), the materialized view exports (SekibanMv) and the sample
// projector (SekibanDcbDeciderSwiftEventSource) into one `.wasm` that the generic
// WasmRuntime.Host loads.
//
// Linker flags (WASI reactor + explicit `--export` per C-ABI symbol) are required because:
//   * the default WASI command model runs `_start` then forbids reentry,
//   * Swift's LTO can strip `@_cdecl` functions unless they're listed in the exports table.
// The `memory` export is emitted automatically by wasm-ld.

let package = Package(
    name: "SekibanDcbDeciderSwiftWasm",
    dependencies: [
        .package(name: "sekiban-swift", path: "../../../wasm-projectors/swift"),
        .package(path: "../SekibanDcbDecider.Swift.EventSource"),
    ],
    targets: [
        .executableTarget(
            name: "SekibanDcbDeciderSwiftWasm",
            dependencies: [
                .product(name: "SekibanWasm", package: "sekiban-swift"),
                .product(name: "SekibanMv", package: "sekiban-swift"),
                .product(name: "SekibanDcbDeciderSwiftEventSource",
                         package: "SekibanDcbDecider.Swift.EventSource"),
            ],
            path: "Sources/SekibanDcbDeciderSwiftWasm",
            linkerSettings: [
                .unsafeFlags([
                    "-Xclang-linker", "-mexec-model=reactor",
                    // `mv_host_query_rows` is an import from `env`, not a host-provided symbol
                    // at link time. wasm-ld needs --import-undefined to turn undefined
                    // `@_silgen_name` decls into module imports rather than link errors.
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
