import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import HTTPTypes
import Hummingbird
import Logging
import NIOCore

// Shared helper: forwards `SerializableQueryRequest` payloads to the generic
// WasmRuntime.Host's `/api/sekiban/serialized/query` / `/api/sekiban/serialized/list-query`
// endpoints and returns the inner `itemsJson` / `resultJson` verbatim. Used by both the
// benchmark read endpoints (BenchmarkReadEndpoints.swift) and the domain-CRUD endpoints
// (ClassRoomWriteEndpoints.swift).

struct QueryForwarder: Sendable {
    let wasmServerUrl: String
    let logger: Logger

    func listQuery(request: Request, queryType: String, params: String) async throws -> Response {
        let waitFor = request.uri.queryParameters["waitForSortableUniqueId"].map { String($0) }
        let body = SerializableQueryRequestBody(
            queryType: queryType,
            queryParamsJson: params,
            waitForSortableUniqueId: waitFor)
        let raw = try await post(path: "/api/sekiban/serialized/list-query", body: body)
        let decoded = try JSONDecoder().decode(SerializedListQueryResponseBody.self, from: raw)
        return queryForwarderJsonRawResponse(decoded.itemsJson)
    }

    func scalarQuery(request: Request, queryType: String, params: String) async throws -> Response {
        let waitFor = request.uri.queryParameters["waitForSortableUniqueId"].map { String($0) }
        let body = SerializableQueryRequestBody(
            queryType: queryType,
            queryParamsJson: params,
            waitForSortableUniqueId: waitFor)
        let raw = try await post(path: "/api/sekiban/serialized/query", body: body)
        let decoded = try JSONDecoder().decode(SerializedQueryResponseBody.self, from: raw)
        return queryForwarderJsonRawResponse(decoded.resultJson)
    }

    /// Fetches the full list and returns the single item whose `jsonIdField` matches the
    /// `idParam` route parameter (case-insensitive). `/api/classrooms/:id` etc. rely on
    /// this because the in-memory projector only exposes a list-query, and re-running the
    /// full projection for every GET keeps the code simple.
    func listQueryFilteredByIdField(
        request: Request,
        context: BasicRequestContext,
        queryType: String,
        idParam: String,
        jsonIdField: String
    ) async throws -> Response {
        let id = context.parameters.get(idParam, as: String.self) ?? ""
        let body = SerializableQueryRequestBody(
            queryType: queryType,
            queryParamsJson: "{}",
            waitForSortableUniqueId: nil)
        let raw = try await post(path: "/api/sekiban/serialized/list-query", body: body)
        let decoded = try JSONDecoder().decode(SerializedListQueryResponseBody.self, from: raw)
        guard let itemsData = decoded.itemsJson.data(using: .utf8),
              let items = try? JSONSerialization.jsonObject(with: itemsData) as? [[String: Any]]
        else {
            return queryForwarderJsonRawResponse("{}")
        }
        let lowerId = id.lowercased()
        if let match = items.first(where: {
            ($0[jsonIdField] as? String)?.lowercased() == lowerId
        }) {
            let data = (try? JSONSerialization.data(withJSONObject: match)) ?? Data("{}".utf8)
            var buffer = ByteBuffer()
            buffer.writeBytes(data)
            return Response(
                status: .ok,
                headers: [.contentType: "application/json"],
                body: ResponseBody(byteBuffer: buffer))
        }
        // 404 shape matches the other samples' "not found" body.
        var buffer = ByteBuffer()
        buffer.writeString(#"{"error":"not found"}"#)
        return Response(
            status: .notFound,
            headers: [.contentType: "application/json"],
            body: ResponseBody(byteBuffer: buffer))
    }

    private func post<Body: Encodable>(path: String, body: Body) async throws -> Data {
        guard let url = URL(string: wasmServerUrl.trimmingSlash() + path) else {
            throw QueryForwarderError.badUrl(wasmServerUrl + path)
        }
        var urlRequest = URLRequest(url: url)
        urlRequest.httpMethod = "POST"
        urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
        urlRequest.httpBody = try JSONEncoder().encode(body)
        let (data, response) = try await URLSession.shared.data(for: urlRequest)
        if let http = response as? HTTPURLResponse, http.statusCode >= 400 {
            let text = String(data: data, encoding: .utf8) ?? "<binary>"
            logger.error("wasmserver \(path) → \(http.statusCode) \(text)")
            throw QueryForwarderError.httpStatus(http.statusCode, text)
        }
        return data
    }
}

struct SerializableQueryRequestBody: Encodable {
    var queryType: String
    var queryParamsJson: String
    var waitForSortableUniqueId: String?
}

struct SerializedListQueryResponseBody: Decodable {
    var itemsJson: String
    var totalCount: Int?
    var totalPages: Int?
    var currentPage: Int?
    var pageSize: Int?
}

struct SerializedQueryResponseBody: Decodable {
    var resultJson: String
}

enum QueryForwarderError: Error, CustomStringConvertible {
    case badUrl(String)
    case httpStatus(Int, String)

    var description: String {
        switch self {
        case let .badUrl(url): return "bad wasmserver url: \(url)"
        case let .httpStatus(code, body): return "wasmserver returned \(code): \(body)"
        }
    }
}

func jsonObject(_ dict: [String: Any]) throws -> String {
    let data = try JSONSerialization.data(withJSONObject: dict, options: [])
    return String(data: data, encoding: .utf8) ?? "{}"
}

func queryForwarderJsonRawResponse(_ raw: String) -> Response {
    var buffer = ByteBuffer()
    buffer.writeString(raw)
    return Response(
        status: .ok,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}

extension String {
    /// Collapse a trailing slash so concatenating "/api/sekiban/..." doesn't double up.
    func trimmingSlash() -> String {
        hasSuffix("/") ? String(dropLast()) : self
    }
}
