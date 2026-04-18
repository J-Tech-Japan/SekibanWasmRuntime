import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

// Reads the current `lastSortableUniqueId` for a single tag by calling the generic
// WasmRuntime.Host's `/api/sekiban/serialized/tag-latest-sortable` endpoint. This is the
// primitive the Rust sample's `HttpCommandContext::tag_exists` / `get_tag_state` helpers
// ride on — pulling the same round-trip into Swift is how the Swift sample matches the
// "real consistency-check" semantics the other runtimes pay for in their commit path.
//
// Wire shape (matches Sekiban's server-side DTO):
//   Request:  {"tag": "Room:<uuid>"}
//   Response: {"exists": true|false, "lastSortableUniqueId": "<string>"}

public struct TagLatestSortableResult: Codable, Sendable, Equatable {
    public var exists: Bool
    public var lastSortableUniqueId: String

    public init(exists: Bool, lastSortableUniqueId: String) {
        self.exists = exists
        self.lastSortableUniqueId = lastSortableUniqueId
    }
}

public enum TagLatestSortableError: Error, CustomStringConvertible {
    case invalidWasmServerUrl(String)
    case encodingFailed(Error)
    case transportFailed(Error)
    case invalidResponse
    case httpStatus(Int, String)

    public var description: String {
        switch self {
        case .invalidWasmServerUrl(let value): return "invalid wasm server URL: \(value)"
        case .encodingFailed(let error): return "JSON encoding failed: \(error)"
        case .transportFailed(let error): return "HTTP transport failed: \(error)"
        case .invalidResponse: return "non-HTTP response"
        case let .httpStatus(code, body): return "wasmserver returned \(code): \(body)"
        }
    }
}

public protocol TagLatestSortableSession: Sendable {
    func post(url: URL, body: Data, contentType: String) async throws -> (Data, Int)
}

public struct URLSessionTagLatestSortableSession: TagLatestSortableSession {
    let session: URLSession
    public init(session: URLSession = .shared) { self.session = session }

    public func post(url: URL, body: Data, contentType: String) async throws -> (Data, Int) {
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue(contentType, forHTTPHeaderField: "Content-Type")
        request.httpBody = body
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw TagLatestSortableError.invalidResponse
        }
        return (data, http.statusCode)
    }
}

public struct TagLatestSortableReader: Sendable {
    public let wasmServerUrl: String
    public let session: TagLatestSortableSession

    public init(
        wasmServerUrl: String,
        session: TagLatestSortableSession = URLSessionTagLatestSortableSession()
    ) {
        self.wasmServerUrl = wasmServerUrl
        self.session = session
    }

    public func read(tag: String) async throws -> TagLatestSortableResult {
        let path = "/api/sekiban/serialized/tag-latest-sortable"
        let base = wasmServerUrl.hasSuffix("/") ? String(wasmServerUrl.dropLast()) : wasmServerUrl
        guard let url = URL(string: base + path) else {
            throw TagLatestSortableError.invalidWasmServerUrl(wasmServerUrl)
        }
        let requestBody: Data
        do {
            requestBody = try JSONEncoder().encode(TagLatestSortableRequest(tag: tag))
        } catch {
            throw TagLatestSortableError.encodingFailed(error)
        }
        let (data, status): (Data, Int)
        do {
            (data, status) = try await session.post(
                url: url,
                body: requestBody,
                contentType: "application/json")
        } catch let known as TagLatestSortableError {
            throw known
        } catch {
            throw TagLatestSortableError.transportFailed(error)
        }
        guard (200..<300).contains(status) else {
            let text = String(data: data, encoding: .utf8) ?? "<binary>"
            throw TagLatestSortableError.httpStatus(status, text)
        }
        do {
            return try JSONDecoder().decode(TagLatestSortableResult.self, from: data)
        } catch {
            // Server responded 200 but with unexpected body — treat as empty tag so the
            // caller can still proceed with `lastSortableUniqueId=""`. Defensive.
            return TagLatestSortableResult(exists: false, lastSortableUniqueId: "")
        }
    }
}

private struct TagLatestSortableRequest: Encodable {
    var tag: String
}
