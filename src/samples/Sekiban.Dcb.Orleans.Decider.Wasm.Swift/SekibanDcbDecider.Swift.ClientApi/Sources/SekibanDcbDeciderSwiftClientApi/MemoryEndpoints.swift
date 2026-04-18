import Foundation
import Hummingbird
import Logging
import NIOCore

// In-memory multi-projection read endpoints. Mirror the role of the MV endpoints, but the
// backing store is the MultiProjectionGrain running inside wasmserver (which drives the
// Swift WASM module's `create_instance`/`apply_event`/`execute_list_query` path). The
// handlers here just forward a SerializableQueryParameter to wasmserver's existing
// `/api/sekiban/serialized/list-query` endpoint and pass the decoded items through.
//
// This gives the Swift sample two parallel read paths:
//   * /api/memory/classrooms → wasmserver → MultiProjectionGrain → Swift WASM (memory)
//   * /api/mv/classrooms     → DcbMaterializedViewPostgres (sqlx direct)
// which is what issue #97 / benchmark matrix #96 need.

func registerMemoryRoutes(
    _ router: Router<BasicRequestContext>,
    wasmServerUrl: String,
    logger: Logger
) {
    router.get("/api/memory/classrooms") { request, _ in
        try await fetchClassroomsFromMemory(
            request: request,
            wasmServerUrl: wasmServerUrl,
            logger: logger)
    }
    router.get("/api/memory/classrooms/count") { request, _ in
        try await fetchClassroomCountFromMemory(
            request: request,
            wasmServerUrl: wasmServerUrl,
            logger: logger)
    }
}

private struct SerializableQueryRequest: Encodable {
    var queryType: String
    var queryParamsJson: String
    var waitForSortableUniqueId: String?

    enum CodingKeys: String, CodingKey {
        case queryType
        case queryParamsJson
        case waitForSortableUniqueId
    }
}

// wasmserver serializes with `JsonSerializerDefaults.Web` so the wire is camelCase, e.g.
// `{"itemsJson":"[{…}]","totalCount":null,…}`. A plain Codable with matching property names
// decodes it directly.
private struct SerializedListQueryResponse: Decodable {
    var itemsJson: String
    var totalCount: Int?
    var totalPages: Int?
    var currentPage: Int?
    var pageSize: Int?
}

private struct SerializedQueryResponse: Decodable {
    var resultJson: String
}

private func fetchClassroomsFromMemory(
    request: Request,
    wasmServerUrl: String,
    logger: Logger
) async throws -> Response {
    let waitFor = request.uri.queryParameters["waitForSortableUniqueId"].map { String($0) }
    let body = SerializableQueryRequest(
        queryType: "GetClassRoomListQuery",
        queryParamsJson: "{}",
        waitForSortableUniqueId: waitFor)
    let raw = try await callWasmServer(
        path: "/api/sekiban/serialized/list-query",
        body: body,
        wasmServerUrl: wasmServerUrl,
        logger: logger)
    let decoded = try JSONDecoder().decode(SerializedListQueryResponse.self, from: raw)
    // The `itemsJson` field is already a JSON array (the Swift projector wrapped its results
    // in {items:[…]} via ListQueryResult). Return it verbatim so the benchmark harness sees
    // the raw projector shape, matching what the Rust sample does.
    return jsonRawResponse(decoded.itemsJson)
}

private func fetchClassroomCountFromMemory(
    request: Request,
    wasmServerUrl: String,
    logger: Logger
) async throws -> Response {
    let waitFor = request.uri.queryParameters["waitForSortableUniqueId"].map { String($0) }
    let body = SerializableQueryRequest(
        queryType: "GetClassRoomCountQuery",
        queryParamsJson: "{}",
        waitForSortableUniqueId: waitFor)
    let raw = try await callWasmServer(
        path: "/api/sekiban/serialized/query",
        body: body,
        wasmServerUrl: wasmServerUrl,
        logger: logger)
    let decoded = try JSONDecoder().decode(SerializedQueryResponse.self, from: raw)
    return jsonRawResponse(decoded.resultJson)
}

private func callWasmServer<Body: Encodable>(
    path: String,
    body: Body,
    wasmServerUrl: String,
    logger: Logger
) async throws -> Data {
    guard let url = URL(string: wasmServerUrl + path) else {
        throw MemoryQueryError.badUrl(wasmServerUrl + path)
    }
    var urlRequest = URLRequest(url: url)
    urlRequest.httpMethod = "POST"
    urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
    urlRequest.httpBody = try JSONEncoder().encode(body)

    let (data, response) = try await URLSession.shared.data(for: urlRequest)
    if let http = response as? HTTPURLResponse, http.statusCode >= 400 {
        let bodyText = String(data: data, encoding: .utf8) ?? "<binary>"
        logger.error("wasmserver \(path) → \(http.statusCode) \(bodyText)")
        throw MemoryQueryError.httpStatus(http.statusCode, bodyText)
    }
    return data
}

enum MemoryQueryError: Error, CustomStringConvertible {
    case badUrl(String)
    case httpStatus(Int, String)

    var description: String {
        switch self {
        case let .badUrl(url): return "bad wasmserver url: \(url)"
        case let .httpStatus(code, body): return "wasmserver returned \(code): \(body)"
        }
    }
}

private func jsonRawResponse(_ raw: String) -> Response {
    var buffer = ByteBuffer()
    buffer.writeString(raw)
    return Response(
        status: .ok,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}
