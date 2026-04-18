import Foundation
import HTTPTypes
import Hummingbird
import Logging
import NIOCore
import SekibanDcbDeciderSwiftClientApiCore

// Benchmark-driver write endpoints. Each handler decodes the driver's JSON body, hands it
// to the reader-backed builder in `SekibanDcbDeciderSwiftClientApiCore`, and forwards the
// resulting `SerializableCommitRequest` to the wasmserver's generic
// `/api/sekiban/serialized/commit` endpoint.
//
// The builder path is deliberately the one that issues tag-latest-sortable round-trips
// and fans events out across multiple tag groups so the Swift numbers are comparable to
// the other language samples. The fresh-ID fast-path builders live in Core but are only
// used from XCTests.

func registerBenchmarkWriteRoutes(
    _ router: Router<BasicRequestContext>,
    wasmServerUrl: String,
    logger: Logger
) {
    let forwarder = CommitRequestForwarder(wasmServerUrl: wasmServerUrl)
    let reader = TagLatestSortableReader(wasmServerUrl: wasmServerUrl)

    router.post("/api/rooms") { request, context in
        try await handleCommit(
            request: request,
            context: context,
            forwarder: forwarder,
            build: { (body: CreateRoomRequest) in
                try buildCreateRoomCommit(request: body).request
            },
            logger: logger)
    }

    router.post("/api/weatherforecast") { request, context in
        try await handleCommit(
            request: request,
            context: context,
            forwarder: forwarder,
            build: { (body: CreateWeatherForecastRequest) in
                try await buildCreateWeatherForecastCommit(request: body, reader: reader).request
            },
            logger: logger)
    }

    router.post("/api/reservations/quick") { request, context in
        try await handleCommit(
            request: request,
            context: context,
            forwarder: forwarder,
            build: { (body: CreateQuickReservationRequest) in
                try await buildCreateQuickReservationCommit(request: body, reader: reader).request
            },
            logger: logger)
    }
}

// ---------------------------------------------------------------------------
// Dispatch helper
// ---------------------------------------------------------------------------

private func handleCommit<B: Decodable>(
    request: Request,
    context: BasicRequestContext,
    forwarder: CommitRequestForwarder,
    build: (B) async throws -> SerializableCommitRequest,
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
        commit = try await build(decoded)
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
        do {
            let sortableField: Any = result.sortableUniqueId.map { $0 as Any } ?? (NSNull() as Any)
            let data = try JSONSerialization.data(
                withJSONObject: [
                    "success": true,
                    "sortableUniqueId": sortableField,
                ],
                options: [])
            var buffer = ByteBuffer()
            buffer.writeBytes(data)
            return Response(
                status: .ok,
                headers: [.contentType: "application/json"],
                body: ResponseBody(byteBuffer: buffer))
        } catch {
            return errorResponse(
                status: .internalServerError,
                message: "failed to encode response: \(error)")
        }
    }

    return passthrough(result)
}

private func passthrough(_ result: CommitForwardResult) -> Response {
    var buffer = ByteBuffer()
    buffer.writeBytes(result.body)
    return Response(
        status: HTTPResponse.Status(code: result.statusCode),
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}

private func errorResponse(status: HTTPResponse.Status, message: String) -> Response {
    let data = (try? JSONSerialization.data(
        withJSONObject: ["success": false, "error": message],
        options: [])) ?? Data(#"{"success":false,"error":"(failed to encode error)"}"#.utf8)
    var buffer = ByteBuffer()
    buffer.writeBytes(data)
    return Response(
        status: status,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}
