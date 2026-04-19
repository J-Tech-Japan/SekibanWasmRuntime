import Foundation
import Hummingbird
import Logging
import PostgresNIO
import NIOCore
import NIOPosix
import SekibanDcbDeciderSwiftClientApiCore

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
        registerMemoryRoutes(
            router,
            wasmServerUrl: context.wasmServerUrl,
            logger: logger)
        registerBenchmarkWriteRoutes(
            router,
            wasmServerUrl: context.wasmServerUrl,
            logger: logger)
        registerBenchmarkReadRoutes(
            router,
            wasmServerUrl: context.wasmServerUrl,
            logger: logger)
        registerDomainRoutes(
            router,
            wasmServerUrl: context.wasmServerUrl,
            logger: logger)

        // Auth is wired only when the MV Postgres client is available — we share that
        // database for the `sekiban_swift_users` table. `SEKIBAN_AUTH_SIGNING_KEY` lets
        // deployments rotate the HMAC secret; dev falls back to a stable value so the
        // cookie remains valid across restarts for manual testing.
        let authSecret = ProcessInfo.processInfo.environment["SEKIBAN_AUTH_SIGNING_KEY"]
            ?? "sekiban-swift-dev-secret-change-me-in-production"
        let authCodec = AuthTokenCodec(secretString: authSecret)
        if let mvClient = mvClient {
            let store = AuthStore(postgres: mvClient, logger: logger)
            registerAuthRoutes(router, store: store, codec: authCodec, logger: logger)
            // Pre-create the users schema at boot so the first /auth/register / /auth/login
            // doesn't race with CREATE TABLE. Use a synchronous Task.detached — the schema
            // ensure is idempotent and the HTTP server starts in parallel.
            Task.detached {
                do {
                    try await store.ensureSchema()
                    logger.info("sekiban_swift_users schema ready")
                } catch {
                    logger.warning("failed to ensure users schema: \(error)")
                }
            }
        } else {
            logger.warning("/auth/* endpoints disabled — no MV Postgres client")
        }

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
