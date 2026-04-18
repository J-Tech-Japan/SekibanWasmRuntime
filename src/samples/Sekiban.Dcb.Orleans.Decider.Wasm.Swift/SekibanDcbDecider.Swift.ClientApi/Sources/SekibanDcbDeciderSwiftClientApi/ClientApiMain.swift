import Foundation
import Hummingbird
import Logging
import PostgresNIO
import NIOCore
import NIOPosix

// Swift ClientApi entry point. Boots a Hummingbird HTTP server on PORT (default 6298) and,
// when the materialized-view Postgres connection string is present, exposes
// /api/mv/{status,classrooms,students,enrollments}. The smoke-test write path lives in
// `build/scripts/seed-swift-mv.sh` — this service is intentionally read-only, mirroring the
// Rust sample's split between write-through (RemoteSekibanExecutor) and read-only MV queries.

@main
struct ClientApiMain {
    static func main() async throws {
        var logger = Logger(label: "SwiftClientApi")
        logger.logLevel = .info
        logger.info("Sekiban DCB Swift ClientApi starting")

        let portString = ProcessInfo.processInfo.environment["PORT"] ?? "6298"
        let port = Int(portString) ?? 6298

        let mvClient: PostgresClient?
        if let info = PostgresConnectionResolver.fromEnvironment() {
            logger.info("MV Postgres configured: host=\(info.host) db=\(info.database)")
            mvClient = PostgresClient(configuration: info.configuration())
        } else {
            logger.warning("No DCBMATERIALIZEDVIEWPOSTGRES_URI / ConnectionStrings__DcbMaterializedViewPostgres; /api/mv/* disabled")
            mvClient = nil
        }

        let context = MvAppContext(
            postgres: mvClient,
            wasmServerUrl: ProcessInfo.processInfo.environment["WASM_SERVER_URL"]
                ?? "http://127.0.0.1:6299",
            logger: logger)

        let router = Router()
        router.get("/health") { _, _ in "ok" }
        registerMvRoutes(router, context: context)

        let app = Application(
            router: router,
            configuration: .init(
                address: .hostname("0.0.0.0", port: port),
                serverName: "sekiban-swift-clientapi"),
            logger: logger)

        // Run the PostgresClient's connection manager alongside the HTTP server.
        try await withThrowingTaskGroup(of: Void.self) { group in
            if let client = mvClient {
                group.addTask {
                    await client.run()
                }
            }
            group.addTask {
                try await app.runService()
            }
            try await group.next()
            group.cancelAll()
        }
    }
}
