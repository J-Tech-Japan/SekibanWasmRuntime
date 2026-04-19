import Foundation
import Logging
import PostgresNIO
import SekibanDcbDeciderSwiftClientApiCore

/// Postgres-backed user store used by the Swift sample's /auth/* endpoints. Schema is
/// created on startup via `ensureSchema()`; the table lives in the MV postgres DB so no
/// extra database is needed (auth is scoped to this sample and does not share users
/// with the template's native ApiService).
///
///   CREATE TABLE sekiban_swift_users (
///     id            TEXT PRIMARY KEY,
///     email         TEXT NOT NULL UNIQUE,
///     password_hash TEXT NOT NULL,
///     display_name  TEXT NOT NULL,
///     created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
///   );
public struct AuthStore: Sendable {
    public let postgres: PostgresClient
    public let logger: Logger
    public let hasher: PasswordHasher

    public init(postgres: PostgresClient, logger: Logger) {
        self.postgres = postgres
        self.logger = logger
        self.hasher = PasswordHasher()
    }

    public func ensureSchema() async throws {
        let ddl: PostgresQuery = """
            CREATE TABLE IF NOT EXISTS sekiban_swift_users (
                id            TEXT PRIMARY KEY,
                email         TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                display_name  TEXT NOT NULL,
                created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
            )
            """
        _ = try await postgres.query(ddl, logger: logger)
    }

    public struct AuthUser: Sendable {
        public let id: String
        public let email: String
        public let displayName: String
    }

    public func register(email: String, password: String, displayName: String?) async throws -> AuthUser {
        try await ensureSchema()
        // Check for dup by email first (SELECT then INSERT keeps error messaging clean).
        let existing: PostgresQuery = """
            SELECT id, email, display_name FROM sekiban_swift_users WHERE email = \(email)
            """
        for try await _ in try await postgres.query(existing, logger: logger) {
            throw AuthStoreError.emailAlreadyRegistered
        }
        let id = UUID().uuidString.lowercased()
        let passwordHash = hasher.hash(password: password)
        let name = displayName?.isEmpty == false ? displayName! : email
        let insert: PostgresQuery = """
            INSERT INTO sekiban_swift_users (id, email, password_hash, display_name)
            VALUES (\(id), \(email), \(passwordHash), \(name))
            """
        _ = try await postgres.query(insert, logger: logger)
        return AuthUser(id: id, email: email, displayName: name)
    }

    public func authenticate(email: String, password: String) async throws -> AuthUser? {
        try await ensureSchema()
        let q: PostgresQuery = """
            SELECT id, email, password_hash, display_name FROM sekiban_swift_users
            WHERE email = \(email)
            """
        for try await row in try await postgres.query(q, logger: logger) {
            let (id, e, hash, name) = try row.decode((String, String, String, String).self)
            if hasher.verify(password: password, encoded: hash) {
                return AuthUser(id: id, email: e, displayName: name)
            }
            return nil
        }
        return nil
    }

    public func findById(_ id: String) async throws -> AuthUser? {
        let q: PostgresQuery = """
            SELECT id, email, display_name FROM sekiban_swift_users WHERE id = \(id)
            """
        for try await row in try await postgres.query(q, logger: logger) {
            let (i, e, n) = try row.decode((String, String, String).self)
            return AuthUser(id: i, email: e, displayName: n)
        }
        return nil
    }
}

public enum AuthStoreError: Error, CustomStringConvertible {
    case emailAlreadyRegistered
    public var description: String {
        switch self {
        case .emailAlreadyRegistered: return "email already registered"
        }
    }
}
