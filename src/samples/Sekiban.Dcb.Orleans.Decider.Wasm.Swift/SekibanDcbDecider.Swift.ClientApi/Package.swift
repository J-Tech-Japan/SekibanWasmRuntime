// swift-tools-version: 6.0
import PackageDescription

// Swift-owned ClientApi for the Sekiban DCB decider Swift sample. Hosts:
//   - /api/mv/{status,classrooms,students,enrollments} — read-only MV queries
//   - /api/rooms, /api/weatherforecast, /api/reservations/quick — write-path commands that
//     construct SerializableCommitRequest JSON and POST to the generic WasmRuntime.Host's
//     /api/sekiban/serialized/commit endpoint (benchmark parity with the Rust/Go/TS samples).
//
// Write-path DTOs + JSON-shape logic live in `SekibanDcbDeciderSwiftClientApiCore` so they
// can be unit-tested without spinning up Hummingbird.

let package = Package(
    name: "SekibanDcbDeciderSwiftClientApi",
    platforms: [.macOS(.v14)],
    dependencies: [
        .package(url: "https://github.com/hummingbird-project/hummingbird.git", from: "2.10.0"),
        .package(url: "https://github.com/vapor/postgres-nio.git", from: "1.27.0"),
        .package(url: "https://github.com/apple/swift-log.git", from: "1.5.0"),
    ],
    targets: [
        .target(
            name: "SekibanDcbDeciderSwiftClientApiCore",
            path: "Sources/SekibanDcbDeciderSwiftClientApiCore"),
        .executableTarget(
            name: "SekibanDcbDeciderSwiftClientApi",
            dependencies: [
                "SekibanDcbDeciderSwiftClientApiCore",
                .product(name: "Hummingbird", package: "hummingbird"),
                .product(name: "PostgresNIO", package: "postgres-nio"),
                .product(name: "Logging", package: "swift-log"),
            ],
            path: "Sources/SekibanDcbDeciderSwiftClientApi"),
        .testTarget(
            name: "SekibanDcbDeciderSwiftClientApiCoreTests",
            dependencies: ["SekibanDcbDeciderSwiftClientApiCore"],
            path: "Tests/SekibanDcbDeciderSwiftClientApiCoreTests"),
    ]
)
