// swift-tools-version: 6.0
import PackageDescription

// Swift-side domain for the Sekiban DCB decider sample. Keeps the event payload Codable
// definitions in one place so both the WASM executable and (future) other Swift consumers can
// share them. Depends only on SekibanMv for the projector protocol and DTOs.
let package = Package(
    name: "SekibanDcbDeciderSwiftEventSource",
    products: [
        .library(
            name: "SekibanDcbDeciderSwiftEventSource",
            type: .static,
            targets: ["SekibanDcbDeciderSwiftEventSource"]),
    ],
    dependencies: [
        .package(name: "sekiban-swift", path: "../../../wasm-projectors/swift"),
    ],
    targets: [
        .target(
            name: "SekibanDcbDeciderSwiftEventSource",
            dependencies: [
                .product(name: "SekibanWasm", package: "sekiban-swift"),
                .product(name: "SekibanMv", package: "sekiban-swift"),
            ],
            path: "Sources/SekibanDcbDeciderSwiftEventSource"),
    ]
)
