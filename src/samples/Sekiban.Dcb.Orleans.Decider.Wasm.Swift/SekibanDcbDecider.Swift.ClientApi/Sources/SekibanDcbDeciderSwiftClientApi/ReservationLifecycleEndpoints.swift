import Foundation
import HTTPTypes
import Hummingbird
import Logging
import NIOCore
import SekibanDcbDeciderSwiftClientApiCore

// Reservation lifecycle + Room update/deactivate/reactivate HTTP routes. Complements
// BenchmarkWriteEndpoints (which only covers the quick-reservation happy path) with the
// template-parity commands the WebNext frontend needs.
//
//   POST /api/reservations/draft                                → CreateReservationDraft
//   POST /api/reservations/{id}/confirm                         → ConfirmReservation
//   POST /api/reservations/{id}/cancel                          → CancelReservation
//   POST /api/reservations/{id}/reject                          → RejectReservation
//   PUT  /api/rooms/{id}                                        → UpdateRoom
//   POST /api/rooms/{id}/deactivate                             → DeactivateRoom
//   POST /api/rooms/{id}/reactivate                             → ReactivateRoom

func registerReservationLifecycleRoutes(
    _ router: Router<BasicRequestContext>,
    wasmServerUrl: String,
    logger: Logger
) {
    let forwarder = CommitRequestForwarder(wasmServerUrl: wasmServerUrl)
    let reader = TagLatestSortableReader(wasmServerUrl: wasmServerUrl)

    router.post("/api/reservations/draft") { request, _ in
        try await lifecycleCommit(
            request: request, forwarder: forwarder, logger: logger,
            build: { (body: CreateReservationDraftRequest) in
                let result = try await buildCreateReservationDraftCommit(request: body, reader: reader)
                return (result.request, ["reservationId": result.reservationId.uuidString.lowercased()])
            })
    }

    router.post("/api/reservations/:id/confirm") { request, context in
        try await lifecycleTransition(
            request: request, context: context,
            forwarder: forwarder, logger: logger,
            build: { reservationId, body in
                try await buildConfirmReservationCommit(
                    request: ReservationLifecycleRequest(
                        reservationId: reservationId,
                        roomId: body.roomId,
                        reason: body.reason),
                    reader: reader)
            })
    }

    router.post("/api/reservations/:id/cancel") { request, context in
        try await lifecycleTransition(
            request: request, context: context,
            forwarder: forwarder, logger: logger,
            build: { reservationId, body in
                try await buildCancelReservationCommit(
                    request: ReservationLifecycleRequest(
                        reservationId: reservationId,
                        roomId: body.roomId,
                        reason: body.reason),
                    reader: reader)
            })
    }

    router.post("/api/reservations/:id/reject") { request, context in
        try await lifecycleTransition(
            request: request, context: context,
            forwarder: forwarder, logger: logger,
            build: { reservationId, body in
                try await buildRejectReservationCommit(
                    request: ReservationLifecycleRequest(
                        reservationId: reservationId,
                        roomId: body.roomId,
                        reason: body.reason),
                    reader: reader)
            })
    }

    // --- Rooms -----------------------------------------------------------

    router.put("/api/rooms/:id") { request, context in
        try await lifecycleCommit(
            request: request, forwarder: forwarder, logger: logger,
            build: { (body: UpdateRoomRequestWire) in
                let roomId = parseUUID(context.parameters.get("id", as: String.self) ?? "")
                    ?? body.roomId ?? UUID()
                let commit = try await buildUpdateRoomCommit(
                    request: UpdateRoomRequest(
                        roomId: roomId,
                        name: body.name,
                        capacity: body.capacity,
                        location: body.location,
                        equipment: body.equipment ?? [],
                        requiresApproval: body.requiresApproval ?? false),
                    reader: reader)
                return (commit, ["roomId": roomId.uuidString.lowercased()])
            })
    }

    router.post("/api/rooms/:id/deactivate") { request, context in
        try await roomLifecycleTransition(
            request: request, context: context,
            forwarder: forwarder, logger: logger,
            build: { roomId, body in
                try await buildDeactivateRoomCommit(
                    request: RoomLifecycleRequest(roomId: roomId, reason: body.reason),
                    reader: reader)
            })
    }

    router.post("/api/rooms/:id/reactivate") { request, context in
        try await roomLifecycleTransition(
            request: request, context: context,
            forwarder: forwarder, logger: logger,
            build: { roomId, _ in
                try await buildReactivateRoomCommit(
                    request: RoomLifecycleRequest(roomId: roomId),
                    reader: reader)
            })
    }
}

// ---------------------------------------------------------------------------
// Shared dispatch helpers (commit + lifecycle transition)
// ---------------------------------------------------------------------------

private func lifecycleCommit<B: Decodable>(
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
        return genericError(.badRequest, "invalid request body: \(error)")
    }
    let commit: SerializableCommitRequest
    let extras: [String: String]
    do {
        (commit, extras) = try await build(decoded)
    } catch {
        return genericError(.internalServerError, "build failed: \(error)")
    }
    return try await forwardCommit(
        commit: commit, forwarder: forwarder, logger: logger, extras: extras)
}

private struct TransitionBody: Decodable {
    var roomId: UUID
    var reason: String?
}

private struct RoomTransitionBody: Decodable {
    var reason: String?
}

private struct UpdateRoomRequestWire: Decodable {
    var roomId: UUID?
    var name: String
    var capacity: Int32
    var location: String
    var equipment: [String]?
    var requiresApproval: Bool?
}

private func lifecycleTransition(
    request: Request,
    context: BasicRequestContext,
    forwarder: CommitRequestForwarder,
    logger: Logger,
    build: (UUID, TransitionBody) async throws -> SerializableCommitRequest
) async throws -> Response {
    guard let reservationId = parseUUID(context.parameters.get("id", as: String.self) ?? "") else {
        return genericError(.badRequest, "invalid reservation id")
    }
    let decoded: TransitionBody
    do {
        let body = try await request.body.collect(upTo: 64 * 1024)
        decoded = try JSONDecoder().decode(TransitionBody.self, from: Data(buffer: body))
    } catch {
        return genericError(.badRequest, "invalid request body: \(error)")
    }
    let commit: SerializableCommitRequest
    do {
        commit = try await build(reservationId, decoded)
    } catch {
        return genericError(.internalServerError, "build failed: \(error)")
    }
    return try await forwardCommit(
        commit: commit, forwarder: forwarder, logger: logger,
        extras: ["reservationId": reservationId.uuidString.lowercased()])
}

private func roomLifecycleTransition(
    request: Request,
    context: BasicRequestContext,
    forwarder: CommitRequestForwarder,
    logger: Logger,
    build: (UUID, RoomTransitionBody) async throws -> SerializableCommitRequest
) async throws -> Response {
    guard let roomId = parseUUID(context.parameters.get("id", as: String.self) ?? "") else {
        return genericError(.badRequest, "invalid room id")
    }
    let decoded: RoomTransitionBody
    do {
        let body = try await request.body.collect(upTo: 64 * 1024)
        if body.readableBytes == 0 {
            decoded = RoomTransitionBody(reason: nil)
        } else {
            decoded = try JSONDecoder().decode(RoomTransitionBody.self, from: Data(buffer: body))
        }
    } catch {
        return genericError(.badRequest, "invalid request body: \(error)")
    }
    let commit: SerializableCommitRequest
    do {
        commit = try await build(roomId, decoded)
    } catch {
        return genericError(.internalServerError, "build failed: \(error)")
    }
    return try await forwardCommit(
        commit: commit, forwarder: forwarder, logger: logger,
        extras: ["roomId": roomId.uuidString.lowercased()])
}

private func forwardCommit(
    commit: SerializableCommitRequest,
    forwarder: CommitRequestForwarder,
    logger: Logger,
    extras: [String: String]
) async throws -> Response {
    let result: CommitForwardResult
    do {
        result = try await forwarder.forward(commit)
    } catch {
        logger.error("commit forward failed: \(error)")
        return genericError(.badGateway, "wasmserver commit failed: \(error)")
    }
    if result.isSuccess {
        var payload: [String: Any] = [
            "success": true,
            "sortableUniqueId": result.sortableUniqueId as Any? ?? NSNull(),
        ]
        for (k, v) in extras { payload[k] = v }
        let data = (try? JSONSerialization.data(withJSONObject: payload))
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

private func genericError(_ status: HTTPResponse.Status, _ message: String) -> Response {
    let data = (try? JSONSerialization.data(
        withJSONObject: ["success": false, "error": message]))
        ?? Data(#"{"success":false}"#.utf8)
    var buffer = ByteBuffer()
    buffer.writeBytes(data)
    return Response(
        status: status,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}

private func parseUUID(_ raw: String) -> UUID? {
    UUID(uuidString: raw)
}
