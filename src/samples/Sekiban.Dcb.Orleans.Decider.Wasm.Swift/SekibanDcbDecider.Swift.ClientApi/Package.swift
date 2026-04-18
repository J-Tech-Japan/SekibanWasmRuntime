// swift-tools-version: 6.0
import PackageDescription

// Swift-owned ClientApi for the Sekiban DCB decider Swift sample. Hosts the read-only
// /api/mv/{status,classrooms,students,enrollments} endpoints, talking to
// DcbMaterializedViewPostgres directly via PostgresNIO. Plays the same role as the Rust
// sample's axum+sqlx clientapi — the generic WasmRuntime.Host stays free of app-specific
// read APIs.
//
// The smoke-test write path is intentionally NOT part of this service — see
// `build/scripts/seed-swift-mv.sh`, which posts SerializableCommitRequest payloads directly
// at the wasmserver's serialized commit endpoint. This scope is allowed by issue #89.

let package = Package(
    name: "SekibanDcbDeciderSwiftClientApi",
    platforms: [.macOS(.v14)],
    dependencies: [
        .package(url: "https://github.com/hummingbird-project/hummingbird.git", from: "2.10.0"),
        .package(url: "https://github.com/vapor/postgres-nio.git", from: "1.27.0"),
        .package(url: "https://github.com/apple/swift-log.git", from: "1.5.0"),
    ],
    targets: [
        .executableTarget(
            name: "SekibanDcbDeciderSwiftClientApi",
            dependencies: [
                .product(name: "Hummingbird", package: "hummingbird"),
                .product(name: "PostgresNIO", package: "postgres-nio"),
                .product(name: "Logging", package: "swift-log"),
            ],
            path: "Sources/SekibanDcbDeciderSwiftClientApi"),
    ]
)
