import Foundation
import PostgresNIO

// Convert Aspire-provided connection strings into a `PostgresClient.Configuration`. Accepts
// the same two shapes as the Rust ClientApi: a ready-to-use postgres URL
// (DCBMATERIALIZEDVIEWPOSTGRES_URI) or an Npgsql-style key/value string
// (ConnectionStrings__DcbMaterializedViewPostgres).
struct PostgresConnectionInfo {
    let host: String
    let port: Int
    let username: String
    let password: String
    let database: String
}

enum PostgresConnectionResolver {
    static func fromEnvironment() -> PostgresConnectionInfo? {
        if let url = ProcessInfo.processInfo.environment["DCBMATERIALIZEDVIEWPOSTGRES_URI"],
           !url.trimmingCharacters(in: .whitespaces).isEmpty,
           let parsed = parseUrl(url) {
            return parsed
        }
        if let raw = ProcessInfo.processInfo.environment["ConnectionStrings__DcbMaterializedViewPostgres"],
           !raw.trimmingCharacters(in: .whitespaces).isEmpty,
           let parsed = parseNpgsql(raw) {
            return parsed
        }
        return nil
    }

    private static func parseUrl(_ url: String) -> PostgresConnectionInfo? {
        guard let components = URLComponents(string: url),
              (components.scheme == "postgres" || components.scheme == "postgresql")
        else { return nil }
        let host = components.host ?? "localhost"
        let port = components.port ?? 5432
        let username = components.user?.removingPercentEncoding ?? "postgres"
        let password = components.password?.removingPercentEncoding ?? ""
        let database = components.path.hasPrefix("/")
            ? String(components.path.dropFirst())
            : components.path
        if database.isEmpty { return nil }
        return PostgresConnectionInfo(
            host: host, port: port, username: username,
            password: password, database: database)
    }

    private static func parseNpgsql(_ raw: String) -> PostgresConnectionInfo? {
        var host: String?
        var port: Int?
        var username: String?
        var password: String?
        var database: String?
        for pair in raw.split(separator: ";") {
            let trimmed = pair.trimmingCharacters(in: .whitespaces)
            guard let eq = trimmed.firstIndex(of: "=") else { continue }
            let key = trimmed[..<eq].lowercased()
            let value = String(trimmed[trimmed.index(after: eq)...])
            switch key {
            case "host", "server": host = value
            case "port": port = Int(value)
            case "username", "user id", "uid": username = value
            case "password", "pwd": password = value
            case "database", "db": database = value
            default: break
            }
        }
        guard let host, let database else { return nil }
        return PostgresConnectionInfo(
            host: host,
            port: port ?? 5432,
            username: username ?? "postgres",
            password: password ?? "",
            database: database)
    }
}

extension PostgresConnectionInfo {
    /// Build a `PostgresClient.Configuration` with unencrypted TCP (Aspire's local Postgres
    /// does not enable TLS by default). For staging/prod, swap `.disable` for `.require`.
    func configuration() -> PostgresClient.Configuration {
        PostgresClient.Configuration(
            host: host,
            port: port,
            username: username,
            password: password,
            database: database,
            tls: .disable)
    }
}
