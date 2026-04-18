import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

// Thin wrapper around URLSession that posts a SerializableCommitRequest to the generic
// WasmRuntime.Host at <wasmServerUrl>/api/sekiban/serialized/commit. Kept separate from the
// Hummingbird layer so tests can exercise the JSON → HTTP contract without a live server
// (by swapping in a fake session).

public struct CommitForwardResult: Sendable {
    public let statusCode: Int
    public let body: Data
    public let sortableUniqueId: String?

    public var isSuccess: Bool { (200..<300).contains(statusCode) }
}

public enum CommitForwardError: Error, CustomStringConvertible {
    case invalidWasmServerUrl(String)
    case encodingFailed(Error)
    case transportFailed(Error)
    case invalidResponse

    public var description: String {
        switch self {
        case .invalidWasmServerUrl(let value):
            return "invalid wasm server URL: \(value)"
        case .encodingFailed(let error):
            return "JSON encoding failed: \(error)"
        case .transportFailed(let error):
            return "HTTP transport failed: \(error)"
        case .invalidResponse:
            return "non-HTTP response"
        }
    }
}

public protocol CommitHTTPSession: Sendable {
    func post(url: URL, body: Data, contentType: String) async throws -> (Data, Int)
}

public struct URLSessionCommitHTTPSession: CommitHTTPSession {
    let session: URLSession

    public init(session: URLSession = .shared) {
        self.session = session
    }

    public func post(url: URL, body: Data, contentType: String) async throws -> (Data, Int) {
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue(contentType, forHTTPHeaderField: "Content-Type")
        request.httpBody = body
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw CommitForwardError.invalidResponse
        }
        return (data, http.statusCode)
    }
}

public struct CommitRequestForwarder: Sendable {
    public let wasmServerUrl: String
    public let session: CommitHTTPSession

    public init(wasmServerUrl: String, session: CommitHTTPSession = URLSessionCommitHTTPSession()) {
        self.wasmServerUrl = wasmServerUrl
        self.session = session
    }

    public func forward(_ request: SerializableCommitRequest) async throws -> CommitForwardResult {
        let path = "/api/sekiban/serialized/commit"
        guard let url = URL(string: wasmServerUrl.appending(path)) else {
            throw CommitForwardError.invalidWasmServerUrl(wasmServerUrl)
        }
        let encoder = makeDefaultCommitJSONEncoder()
        let body: Data
        do {
            body = try encoder.encode(request)
        } catch {
            throw CommitForwardError.encodingFailed(error)
        }
        let (data, statusCode): (Data, Int)
        do {
            (data, statusCode) = try await session.post(
                url: url,
                body: body,
                contentType: "application/json")
        } catch let forwardError as CommitForwardError {
            throw forwardError
        } catch {
            throw CommitForwardError.transportFailed(error)
        }
        return CommitForwardResult(
            statusCode: statusCode,
            body: data,
            sortableUniqueId: extractSortableUniqueId(from: data))
    }
}

private func extractSortableUniqueId(from body: Data) -> String? {
    guard let json = try? JSONSerialization.jsonObject(with: body) as? [String: Any] else {
        return nil
    }
    if let direct = json["sortableUniqueId"] as? String {
        return direct
    }
    // Orleans returns `{ success, writtenEvents: [{sortableUniqueId, ...}, ...] }`. Take
    // the last event's sortable id, matching what the benchmark driver looks for.
    if let events = json["writtenEvents"] as? [[String: Any]], !events.isEmpty,
       let id = events.last?["sortableUniqueId"] as? String
    {
        return id
    }
    return nil
}

extension String {
    fileprivate func appending(_ other: String) -> String {
        if hasSuffix("/") && other.hasPrefix("/") {
            return self + other.dropFirst()
        }
        if !hasSuffix("/") && !other.hasPrefix("/") {
            return self + "/" + other
        }
        return self + other
    }
}
