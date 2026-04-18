import Foundation
import HTTPTypes
import Hummingbird
import Logging
import NIOCore
import SekibanDcbDeciderSwiftClientApiCore

// Benchmark-driver write endpoints. Each handler decodes the driver's JSON body, hands it
// to the corresponding builder in `SekibanDcbDeciderSwiftClientApiCore`, and forwards the
// resulting `SerializableCommitRequest` to the wasmserver's generic
// `/api/sekiban/serialized/commit` endpoint. Events match the Rust sample's wire shape so
// the same WasmRuntime.Host accepts them identically.
//
// Routes mounted here:
//   POST /api/rooms                       → RoomCreated
//   POST /api/weatherforecast             → WeatherForecastCreated
//   POST /api/reservations/quick          → ReservationDraftCreated + Held + Confirmed
//
// These endpoints intentionally skip the Rust sample's "AlreadyExists" / "RoomNotFound"
// precondition checks — the benchmark driver always mints fresh entity IDs, so the guard
// is a no-op for brand-new tags. State reconstruction via the WASM tag-state projector is
// left to the host and is not needed for the happy path.

func registerBenchmarkWriteRoutes(
    _ router: Router<BasicRequestContext>,
    wasmServerUrl: String,
    logger: Logger
) {
    let forwarder = CommitRequestForwarder(wasmServerUrl: wasmServerUrl)

    router.post("/api/rooms") { request, context in
        try await handleCommit(
            request: request,
            context: context,
            forwarder: forwarder,
            build: { (body: CreateRoomRequest) in
                try buildCreateRoomCommit(request: body).request
            },
            successBody: { result in
                // Match Rust's response envelope: { success, roomId, sortableUniqueId, eventId }
                [
                    "success": result.isSuccess,
                    "sortableUniqueId": result.sortableUniqueId ?? NSNull(),
                ] as [String: Any]
            },
            logger: logger)
    }

    router.post("/api/weatherforecast") { request, context in
        try await handleCommit(
            request: request,
            context: context,
            forwarder: forwarder,
            build: { (body: CreateWeatherForecastRequest) in
                try buildCreateWeatherForecastCommit(request: body).request
            },
            successBody: { result in
                [
                    "success": result.isSuccess,
                    "sortableUniqueId": result.sortableUniqueId ?? NSNull(),
                ]
            },
            logger: logger)
    }

    router.post("/api/reservations/quick") { request, context in
        try await handleCommit(
            request: request,
            context: context,
            forwarder: forwarder,
            build: { (body: CreateQuickReservationRequest) in
                try buildCreateQuickReservationCommit(request: body).request
            },
            successBody: { result in
                [
                    "success": result.isSuccess,
                    "sortableUniqueId": result.sortableUniqueId ?? NSNull(),
                ]
            },
            logger: logger)
    }
}

// ---------------------------------------------------------------------------
// Dispatch helper
// ---------------------------------------------------------------------------

/// Read the request body, decode it into `B`, call `build` to produce a commit request,
/// forward it to wasmserver, and render the forwarder result (HTTP pass-through for
/// non-200 codes so the benchmark driver sees the real failure).
private func handleCommit<B: Decodable>(
    request: Request,
    context: BasicRequestContext,
    forwarder: CommitRequestForwarder,
    build: (B) throws -> SerializableCommitRequest,
    successBody: (CommitForwardResult) -> [String: Any],
    logger: Logger
) async throws -> Response {
    let decoded: B
    do {
        let body = try await request.body.collect(upTo: 1024 * 1024)
        decoded = try JSONDecoder().decode(B.self, from: Data(buffer: body))
    } catch {
        return errorResponse(
            status: .badRequest,
            message: "invalid request body: \(error)")
    }

    let commit: SerializableCommitRequest
    do {
        commit = try build(decoded)
    } catch {
        return errorResponse(
            status: .internalServerError,
            message: "failed to build commit request: \(error)")
    }

    let result: CommitForwardResult
    do {
        result = try await forwarder.forward(commit)
    } catch {
        logger.error("commit forward failed: \(error)")
        return errorResponse(
            status: .badGateway,
            message: "wasmserver commit failed: \(error)")
    }

    if result.isSuccess {
        let body = successBody(result)
        return try jsonDictResponse(body, status: .ok)
    }

    // Non-2xx from wasmserver: pass the body through so the benchmark driver can see the
    // error message. Map the status code 1-to-1.
    return passthrough(result)
}

private func passthrough(_ result: CommitForwardResult) -> Response {
    var buffer = ByteBuffer()
    buffer.writeBytes(result.body)
    let status: HTTPResponse.Status = HTTPResponse.Status(code: result.statusCode)
    return Response(
        status: status,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}

private func errorResponse(status: HTTPResponse.Status, message: String) -> Response {
    do {
        let data = try JSONSerialization.data(
            withJSONObject: ["success": false, "error": message],
            options: [])
        var buffer = ByteBuffer()
        buffer.writeBytes(data)
        return Response(
            status: status,
            headers: [.contentType: "application/json"],
            body: ResponseBody(byteBuffer: buffer))
    } catch {
        var buffer = ByteBuffer()
        buffer.writeString(#"{"success":false,"error":"(failed to encode error)"}"#)
        return Response(
            status: status,
            headers: [.contentType: "application/json"],
            body: ResponseBody(byteBuffer: buffer))
    }
}

private func jsonDictResponse(_ dict: [String: Any], status: HTTPResponse.Status) throws -> Response {
    let data = try JSONSerialization.data(withJSONObject: dict, options: [])
    var buffer = ByteBuffer()
    buffer.writeBytes(data)
    return Response(
        status: status,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}
