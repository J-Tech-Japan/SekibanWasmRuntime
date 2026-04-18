import Foundation
import HTTPTypes
import Hummingbird
import Logging
import NIOCore
import SekibanDcbDeciderSwiftClientApiCore

// Benchmark-driver read endpoints. Pairs with `BenchmarkWriteEndpoints.swift`. Each GET
// forwards a `SerializableQueryRequest` to the wasmserver's serialized query/list-query
// endpoints and returns the projector's items JSON verbatim (the benchmark driver decodes
// it as an array).
//
// Routes:
//   GET /api/rooms                                  → GetRoomListQuery (list)
//   GET /api/reservations?pageNumber=&pageSize=     → GetReservationListQuery (list)
//   GET /api/reservations/by-room/{roomId}          → GetReservationsByRoomQuery (list)
//   GET /api/weatherforecast                        → GetWeatherForecastListQuery (list)
//   GET /api/weatherforecast/count                  → GetWeatherForecastCountQuery (scalar)

func registerBenchmarkReadRoutes(
    _ router: Router<BasicRequestContext>,
    wasmServerUrl: String,
    logger: Logger
) {
    let forwarder = QueryForwarder(wasmServerUrl: wasmServerUrl, logger: logger)

    router.get("/api/rooms") { request, _ in
        try await forwarder.listQuery(
            request: request,
            queryType: "GetRoomListQuery",
            params: "{}")
    }

    router.get("/api/reservations") { request, _ in
        try await forwarder.listQuery(
            request: request,
            queryType: "GetReservationListQuery",
            params: "{}")
    }

    router.get("/api/reservations/by-room/:roomId") { request, context in
        let roomId = context.parameters.get("roomId", as: String.self) ?? ""
        let params = try jsonObject(["roomId": roomId])
        return try await forwarder.listQuery(
            request: request,
            queryType: "GetReservationsByRoomQuery",
            params: params)
    }

    router.get("/api/weatherforecast") { request, _ in
        try await forwarder.listQuery(
            request: request,
            queryType: "GetWeatherForecastListQuery",
            params: "{}")
    }

    router.get("/api/weatherforecast/count") { request, _ in
        try await forwarder.scalarQuery(
            request: request,
            queryType: "GetWeatherForecastCountQuery",
            params: "{}")
    }
}

// ---------------------------------------------------------------------------
// Forwarder
// ---------------------------------------------------------------------------

fileprivate struct QueryForwarder: Sendable {
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
        return jsonRawResponse(decoded.itemsJson)
    }

    func scalarQuery(request: Request, queryType: String, params: String) async throws -> Response {
        let waitFor = request.uri.queryParameters["waitForSortableUniqueId"].map { String($0) }
        let body = SerializableQueryRequestBody(
            queryType: queryType,
            queryParamsJson: params,
            waitForSortableUniqueId: waitFor)
        let raw = try await post(path: "/api/sekiban/serialized/query", body: body)
        let decoded = try JSONDecoder().decode(SerializedQueryResponseBody.self, from: raw)
        return jsonRawResponse(decoded.resultJson)
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

fileprivate struct SerializableQueryRequestBody: Encodable {
    var queryType: String
    var queryParamsJson: String
    var waitForSortableUniqueId: String?
}

fileprivate struct SerializedListQueryResponseBody: Decodable {
    var itemsJson: String
    var totalCount: Int?
    var totalPages: Int?
    var currentPage: Int?
    var pageSize: Int?
}

fileprivate struct SerializedQueryResponseBody: Decodable {
    var resultJson: String
}

fileprivate enum QueryForwarderError: Error, CustomStringConvertible {
    case badUrl(String)
    case httpStatus(Int, String)

    var description: String {
        switch self {
        case let .badUrl(url): return "bad wasmserver url: \(url)"
        case let .httpStatus(code, body): return "wasmserver returned \(code): \(body)"
        }
    }
}

fileprivate func jsonObject(_ dict: [String: Any]) throws -> String {
    let data = try JSONSerialization.data(withJSONObject: dict, options: [])
    return String(data: data, encoding: .utf8) ?? "{}"
}

fileprivate func jsonRawResponse(_ raw: String) -> Response {
    var buffer = ByteBuffer()
    buffer.writeString(raw)
    return Response(
        status: .ok,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}

fileprivate extension String {
    /// Collapse a trailing slash so concatenating "/api/sekiban/..." doesn't double up.
    func trimmingSlash() -> String {
        hasSuffix("/") ? String(dropLast()) : self
    }
}
