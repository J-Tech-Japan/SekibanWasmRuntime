import Foundation
import HTTPTypes
import Hummingbird
import Logging
import NIOCore
import SekibanDcbDeciderSwiftClientApiCore

// ClassRoom / Student / Enrollment / Weather update+delete HTTP routes. Endpoint paths
// match what the Sekiban.Dcb.Orleans.Decider template's Web + WebNext frontends expect so
// those frontends can be pointed straight at the Swift ClientApi:
//
//   POST /api/classrooms              → CreateClassRoom
//   GET  /api/classrooms              → GetClassRoomListQuery
//   GET  /api/classrooms/{id}         → GetClassRoom (from list, filtered)
//   POST /api/students                → CreateStudent
//   GET  /api/students                → GetStudentListQuery
//   GET  /api/students/{id}           → GetStudent (from list, filtered)
//   POST /api/enrollments/add         → EnrollStudentInClassRoom
//   POST /api/enrollments/drop        → DropStudentFromClassRoom
//   GET  /api/enrollments             → GetEnrollmentListQuery (student_id / class_room_id filters)
//   POST /api/inputweatherforecast    → alias for /api/weatherforecast (CreateWeatherForecast)
//   POST /api/updateweatherforecastlocation
//   POST /api/removeweatherforecast

func registerDomainRoutes(
    _ router: Router<BasicRequestContext>,
    wasmServerUrl: String,
    logger: Logger
) {
    let forwarder = CommitRequestForwarder(wasmServerUrl: wasmServerUrl)
    let reader = TagLatestSortableReader(wasmServerUrl: wasmServerUrl)
    let queryForwarder = QueryForwarder(wasmServerUrl: wasmServerUrl, logger: logger)

    // --- ClassRooms ---------------------------------------------------------

    router.post("/api/classrooms") { request, _ in
        try await handleDomainCommit(
            request: request,
            forwarder: forwarder,
            logger: logger,
            build: { (body: CreateClassRoomRequest) in
                let result = try await buildCreateClassRoomCommit(request: body, reader: reader)
                return (result.request, ["classRoomId": result.classRoomId.uuidString.lowercased()])
            })
    }

    router.get("/api/classrooms") { request, _ in
        try await queryForwarder.listQuery(
            request: request,
            queryType: "GetClassRoomListQuery",
            params: paramsFromQuery(request))
    }

    router.get("/api/classrooms/:id") { request, context in
        try await queryForwarder.listQueryFilteredByIdField(
            request: request,
            context: context,
            queryType: "GetClassRoomListQuery",
            idParam: "id",
            jsonIdField: "class_room_id")
    }

    // --- Students -----------------------------------------------------------

    router.post("/api/students") { request, _ in
        try await handleDomainCommit(
            request: request,
            forwarder: forwarder,
            logger: logger,
            build: { (body: CreateStudentRequest) in
                let result = try await buildCreateStudentCommit(request: body, reader: reader)
                return (result.request, ["studentId": result.studentId.uuidString.lowercased()])
            })
    }

    router.get("/api/students") { request, _ in
        try await queryForwarder.listQuery(
            request: request,
            queryType: "GetStudentListQuery",
            params: paramsFromQuery(request))
    }

    router.get("/api/students/:id") { request, context in
        try await queryForwarder.listQueryFilteredByIdField(
            request: request,
            context: context,
            queryType: "GetStudentListQuery",
            idParam: "id",
            jsonIdField: "student_id")
    }

    // --- Enrollments --------------------------------------------------------

    router.post("/api/enrollments/add") { request, _ in
        try await handleDomainCommit(
            request: request,
            forwarder: forwarder,
            logger: logger,
            build: { (body: EnrollmentCommandRequest) in
                let result = try await buildEnrollStudentCommit(request: body, reader: reader)
                return (result.request, [:])
            })
    }

    router.post("/api/enrollments/drop") { request, _ in
        try await handleDomainCommit(
            request: request,
            forwarder: forwarder,
            logger: logger,
            build: { (body: EnrollmentCommandRequest) in
                let result = try await buildDropStudentCommit(request: body, reader: reader)
                return (result.request, [:])
            })
    }

    router.get("/api/enrollments") { request, _ in
        let studentId = request.uri.queryParameters["studentId"]
            ?? request.uri.queryParameters["student_id"]
        let classRoomId = request.uri.queryParameters["classRoomId"]
            ?? request.uri.queryParameters["class_room_id"]
        var paramDict: [String: String] = [:]
        if let v = studentId { paramDict["studentId"] = String(v) }
        if let v = classRoomId { paramDict["classRoomId"] = String(v) }
        let params = (try? JSONSerialization.data(withJSONObject: paramDict))
            .flatMap { String(data: $0, encoding: .utf8) }
            ?? "{}"
        return try await queryForwarder.listQuery(
            request: request,
            queryType: "GetEnrollmentListQuery",
            params: params)
    }

    // --- Weather update/delete (template parity) ---------------------------

    router.post("/api/inputweatherforecast") { request, _ in
        // Alias for POST /api/weatherforecast — the Blazor template uses both paths
        // (historical naming divergence between native and WASM samples).
        try await handleDomainCommit(
            request: request,
            forwarder: forwarder,
            logger: logger,
            build: { (body: CreateWeatherForecastRequest) in
                let result = try await buildCreateWeatherForecastCommit(request: body, reader: reader)
                return (result.request, ["forecastId": result.forecastId.uuidString.lowercased()])
            })
    }

    router.post("/api/updateweatherforecastlocation") { request, _ in
        try await handleDomainCommit(
            request: request,
            forwarder: forwarder,
            logger: logger,
            build: { (body: UpdateWeatherForecastLocationRequest) in
                let commit = try await buildUpdateWeatherForecastLocationCommit(
                    request: body, reader: reader)
                return (commit, ["forecastId": body.forecastId.uuidString.lowercased()])
            })
    }

    router.post("/api/removeweatherforecast") { request, _ in
        try await handleDomainCommit(
            request: request,
            forwarder: forwarder,
            logger: logger,
            build: { (body: DeleteWeatherForecastRequest) in
                let commit = try await buildDeleteWeatherForecastCommit(
                    request: body, reader: reader)
                return (commit, ["forecastId": body.forecastId.uuidString.lowercased()])
            })
    }
}

// ---------------------------------------------------------------------------
// Dispatch helper (mirrors BenchmarkWriteEndpoints.handleCommit but lets each
// handler attach extra fields to the JSON response — roomId / classRoomId etc.
// which the template frontends read out of the body).
// ---------------------------------------------------------------------------

private func handleDomainCommit<B: Decodable>(
    request: Request,
    forwarder: CommitRequestForwarder,
    logger: Logger,
    build: (B) async throws -> (SerializableCommitRequest, [String: String])
) async throws -> Response {
    let decoded: B
    do {
        let body = try await request.body.collect(upTo: 1024 * 1024)
        decoded = try JSONDecoder().decode(B.self, from: Data(buffer: body))
    } catch {
        return Response(
            status: .badRequest,
            headers: [.contentType: "application/json"],
            body: errorBody("invalid request body: \(error)"))
    }
    let commit: SerializableCommitRequest
    let extras: [String: String]
    do {
        (commit, extras) = try await build(decoded)
    } catch {
        return Response(
            status: .internalServerError,
            headers: [.contentType: "application/json"],
            body: errorBody("failed to build commit request: \(error)"))
    }
    let result: CommitForwardResult
    do {
        result = try await forwarder.forward(commit)
    } catch {
        logger.error("commit forward failed: \(error)")
        return Response(
            status: .badGateway,
            headers: [.contentType: "application/json"],
            body: errorBody("wasmserver commit failed: \(error)"))
    }
    if result.isSuccess {
        var response: [String: Any] = [
            "success": true,
            "sortableUniqueId": result.sortableUniqueId as Any? ?? NSNull(),
        ]
        for (k, v) in extras { response[k] = v }
        let data = (try? JSONSerialization.data(withJSONObject: response))
            ?? Data(#"{"success":true}"#.utf8)
        var buffer = ByteBuffer()
        buffer.writeBytes(data)
        return Response(
            status: .ok,
            headers: [.contentType: "application/json"],
            body: ResponseBody(byteBuffer: buffer))
    }
    var buffer = ByteBuffer()
    buffer.writeBytes(result.body)
    return Response(
        status: HTTPResponse.Status(code: result.statusCode),
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}

private func errorBody(_ message: String) -> ResponseBody {
    let data = (try? JSONSerialization.data(
        withJSONObject: ["success": false, "error": message]))
        ?? Data(#"{"success":false}"#.utf8)
    var buffer = ByteBuffer()
    buffer.writeBytes(data)
    return ResponseBody(byteBuffer: buffer)
}

private func paramsFromQuery(_ request: Request) -> String {
    var dict: [String: String] = [:]
    for (key, value) in request.uri.queryParameters {
        dict[String(key)] = String(value)
    }
    if dict.isEmpty { return "{}" }
    let data = (try? JSONSerialization.data(withJSONObject: dict)) ?? Data("{}".utf8)
    return String(data: data, encoding: .utf8) ?? "{}"
}
