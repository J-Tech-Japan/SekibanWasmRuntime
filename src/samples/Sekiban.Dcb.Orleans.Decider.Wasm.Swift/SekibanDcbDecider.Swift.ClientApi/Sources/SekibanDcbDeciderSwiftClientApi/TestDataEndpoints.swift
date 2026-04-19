import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import HTTPTypes
import Hummingbird
import Logging
import NIOCore
import SekibanDcbDeciderSwiftClientApiCore

// /api/test-data/* — creates a demo corpus of rooms + reservations in one call, matching
// the shape the WebNext frontend's `testData.generate` tRPC procedure expects. The native
// template's equivalent lives in SekibanDcbDecider.ApiService/Endpoints/TestDataEndpoints.cs;
// we reuse the existing CreateRoom / CreateQuickReservation builders so event shapes stay
// identical to every other write path in this sample.

func registerTestDataRoutes(
    _ router: Router<BasicRequestContext>,
    wasmServerUrl: String,
    logger: Logger
) {
    let forwarder = CommitRequestForwarder(wasmServerUrl: wasmServerUrl)
    let reader = TagLatestSortableReader(wasmServerUrl: wasmServerUrl)

    router.post("/api/test-data/generate") { request, _ in
        let tzMinutes = queryInt(request, key: "timeZoneOffsetMinutes")
        return try await generateTestDataResponse(
            forwarder: forwarder, reader: reader, logger: logger,
            wasmServerUrl: wasmServerUrl,
            timeZoneOffsetMinutes: tzMinutes,
            includeReservations: true,
            onlyRoomIds: nil)
    }

    router.post("/api/test-data/generate-rooms") { _, _ in
        try await generateTestDataResponse(
            forwarder: forwarder, reader: reader, logger: logger,
            wasmServerUrl: wasmServerUrl,
            timeZoneOffsetMinutes: nil,
            includeReservations: false,
            onlyRoomIds: nil)
    }

    router.post("/api/test-data/generate-reservations") { request, _ in
        let tzMinutes = queryInt(request, key: "timeZoneOffsetMinutes")
        let explicitRoomId = queryString(request, key: "roomId").flatMap(UUID.init(uuidString:))
        let forcedRooms: [UUID]? = explicitRoomId.map { [$0] }
        return try await generateTestDataResponse(
            forwarder: forwarder, reader: reader, logger: logger,
            wasmServerUrl: wasmServerUrl,
            timeZoneOffsetMinutes: tzMinutes,
            includeReservations: true,
            onlyRoomIds: forcedRooms)
    }
}

// ---------------------------------------------------------------------------
// Core helper
// ---------------------------------------------------------------------------

private struct TestDataResult: Encodable {
    var roomsCreated: Int
    var roomIds: [String]
    var reservationsCreated: Int
    var reservationIds: [String]
    var errors: [String]
}

private let roomDefinitions: [(name: String, capacity: Int32, location: String, equipment: [String], requiresApproval: Bool)] = [
    ("Conference Room A", 20, "Building 1, Floor 2", ["Projector", "Whiteboard", "Video Conference"], false),
    ("Meeting Room B", 8, "Building 1, Floor 3", ["TV Screen", "Whiteboard"], false),
    ("Executive Boardroom", 16, "Building 2, Floor 5", ["Projector", "Video Conference", "Sound System", "Recording"], true),
    ("Huddle Space 1", 4, "Building 1, Floor 1", ["TV Screen"], false),
    ("Training Room", 30, "Building 3, Floor 1", ["Projector", "Multiple Screens", "Recording", "Microphones"], true),
    ("Small Meeting Room C", 6, "Building 1, Floor 2", ["Whiteboard"], false),
]

private let reservationDefinitions: [(roomIndex: Int, daysOffset: Int, startHour: Int, endHour: Int, purpose: String)] = [
    (0, 0, 9, 10, "Team Standup"),
    (0, 0, 14, 16, "Sprint Planning"),
    (1, 1, 10, 11, "1:1 Meeting"),
    (2, 1, 13, 15, "Board Meeting"),
    (4, 3, 9, 17, "All-hands Training"),
]

private func generateTestDataResponse(
    forwarder: CommitRequestForwarder,
    reader: TagLatestSortableReader,
    logger: Logger,
    wasmServerUrl: String,
    timeZoneOffsetMinutes: Int?,
    includeReservations: Bool,
    onlyRoomIds: [UUID]?
) async throws -> Response {
    var result = TestDataResult(
        roomsCreated: 0, roomIds: [], reservationsCreated: 0,
        reservationIds: [], errors: [])
    var lastSortableUniqueId: String?

    let roomIds: [UUID]
    if let forced = onlyRoomIds {
        roomIds = forced
    } else {
        var created: [UUID] = []
        for def in roomDefinitions {
            do {
                let build = try buildCreateRoomCommit(
                    request: CreateRoomRequest(
                        roomId: UUID(),
                        name: def.name,
                        capacity: def.capacity,
                        location: def.location,
                        equipment: def.equipment,
                        requiresApproval: def.requiresApproval))
                let commitResult = try await forwarder.forward(build.request)
                if commitResult.isSuccess {
                    created.append(build.roomId)
                    if let suid = commitResult.sortableUniqueId { lastSortableUniqueId = suid }
                } else {
                    let body = String(decoding: commitResult.body, as: UTF8.self)
                    result.errors.append("room '\(def.name)' status=\(commitResult.statusCode): \(body)")
                }
            } catch {
                result.errors.append("room '\(def.name)' failed: \(error)")
            }
        }
        roomIds = created
    }
    result.roomsCreated = roomIds.count
    result.roomIds = roomIds.map { $0.uuidString.lowercased() }

    if includeReservations, !roomIds.isEmpty {
        let organizerId = UUID()
        let organizerName = "Sample User"
        let (baseDate, offsetSeconds) = resolveLocalBaseDate(timeZoneOffsetMinutes: timeZoneOffsetMinutes)
        let firstReservableDay = baseDate.addingTimeInterval(24 * 60 * 60)

        for def in reservationDefinitions {
            guard def.roomIndex < roomIds.count else {
                logger.warning("skipping reservation '\(def.purpose)': room index \(def.roomIndex) ≥ \(roomIds.count)")
                continue
            }
            let roomId = roomIds[def.roomIndex]
            let start = firstReservableDay
                .addingTimeInterval(Double(def.daysOffset) * 24 * 60 * 60)
                .addingTimeInterval(Double(def.startHour) * 60 * 60)
            let end = firstReservableDay
                .addingTimeInterval(Double(def.daysOffset) * 24 * 60 * 60)
                .addingTimeInterval(Double(def.endHour) * 60 * 60)
            // Convert from local wall-clock back to UTC by subtracting the offset.
            let startUtc = start.addingTimeInterval(-offsetSeconds)
            let endUtc = end.addingTimeInterval(-offsetSeconds)

            do {
                let build = try await buildCreateQuickReservationCommit(
                    request: CreateQuickReservationRequest(
                        reservationId: UUID(),
                        roomId: roomId,
                        organizerId: organizerId,
                        organizerName: organizerName,
                        startTime: SekibanTime.iso8601(from: startUtc),
                        endTime: SekibanTime.iso8601(from: endUtc),
                        attendeeCount: nil,
                        purpose: def.purpose,
                        selectedEquipment: nil),
                    reader: reader)
                let commitResult = try await forwarder.forward(build.request)
                if commitResult.isSuccess {
                    result.reservationsCreated += 1
                    result.reservationIds.append(build.reservationId.uuidString.lowercased())
                    if let suid = commitResult.sortableUniqueId { lastSortableUniqueId = suid }
                } else {
                    let body = String(decoding: commitResult.body, as: UTF8.self)
                    result.errors.append("reservation '\(def.purpose)' status=\(commitResult.statusCode): \(body)")
                }
            } catch {
                result.errors.append("reservation '\(def.purpose)' failed: \(error)")
            }
        }
    }

    // Block until the Room and Reservation projections have consumed the last commit.
    // Without this the Next.js UI's refetch races the projector and shows 0 rooms /
    // 0 reservations for a second or two after a successful "Generate Test Data" click.
    if let suid = lastSortableUniqueId {
        await waitForProjectionCatchUp(
            wasmServerUrl: wasmServerUrl,
            queryType: "GetRoomListQuery",
            sortableUniqueId: suid,
            logger: logger)
        if includeReservations {
            await waitForProjectionCatchUp(
                wasmServerUrl: wasmServerUrl,
                queryType: "GetReservationListQuery",
                sortableUniqueId: suid,
                logger: logger)
        }
    }

    let data = try JSONEncoder().encode(result)
    var buffer = ByteBuffer()
    buffer.writeBytes(data)
    return Response(
        status: .ok,
        headers: [.contentType: "application/json"],
        body: ResponseBody(byteBuffer: buffer))
}

// Returns (midnight-local-today-as-UTC-instant, secondsEastOfUtc) so callers can
// compose local hours and then convert back to UTC by subtracting the offset.
private func resolveLocalBaseDate(timeZoneOffsetMinutes: Int?) -> (Date, Double) {
    // JS `Date.getTimezoneOffset()` returns UTC-minus-local in minutes — so a UTC caller
    // sends 0, and a JST (UTC+9) caller sends -540. Invert the sign to get seconds east
    // of UTC, matching the C# template's convention.
    let offsetSeconds: Double
    if let m = timeZoneOffsetMinutes {
        offsetSeconds = Double(-m) * 60.0
    } else {
        offsetSeconds = Double(TimeZone.current.secondsFromGMT())
    }
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = TimeZone(secondsFromGMT: Int(offsetSeconds)) ?? .current
    let startOfDay = calendar.startOfDay(for: Date())
    return (startOfDay, offsetSeconds)
}

/// Issues a `/api/sekiban/serialized/list-query` with `waitForSortableUniqueId` set and
/// ignores the body. This is how we force the wasmserver's projection manager to apply
/// the given event before returning from /api/test-data/generate — without it, the
/// Next.js UI refetches before the projector catches up and shows an empty list.
/// Uses the same `URLSession.shared.data(for:)` path as `QueryForwarder.post` so
/// behavior is identical on macOS and Linux (`FoundationNetworking`).
private func waitForProjectionCatchUp(
    wasmServerUrl: String,
    queryType: String,
    sortableUniqueId: String,
    logger: Logger
) async {
    guard let url = URL(string: wasmServerUrl.trimmingSlash() + "/api/sekiban/serialized/list-query") else {
        return
    }
    var urlRequest = URLRequest(url: url)
    urlRequest.httpMethod = "POST"
    urlRequest.setValue("application/json", forHTTPHeaderField: "Content-Type")
    let payload: [String: Any] = [
        "queryType": queryType,
        "queryParamsJson": "{}",
        "waitForSortableUniqueId": sortableUniqueId,
    ]
    urlRequest.httpBody = (try? JSONSerialization.data(withJSONObject: payload))
        ?? Data(#"{"queryType":"\#(queryType)","queryParamsJson":"{}","waitForSortableUniqueId":"\#(sortableUniqueId)"}"#.utf8)
    do {
        // URLSession does not throw on non-2xx — inspect the response explicitly so a
        // 4xx/5xx from wasmserver doesn't silently masquerade as a successful catch-up.
        let (data, response) = try await URLSession.shared.data(for: urlRequest)
        if let http = response as? HTTPURLResponse, http.statusCode >= 400 {
            let text = String(data: data, encoding: .utf8) ?? "<non-utf8>"
            logger.warning(
                "waitForProjectionCatchUp(\(queryType)) → \(http.statusCode) \(text)")
        }
    } catch {
        logger.warning("waitForProjectionCatchUp(\(queryType)) failed: \(error)")
    }
}

private func queryInt(_ request: Request, key: String) -> Int? {
    guard let raw = request.uri.queryParameters[Substring(key)] else { return nil }
    return Int(String(raw))
}

private func queryString(_ request: Request, key: String) -> String? {
    guard let raw = request.uri.queryParameters[Substring(key)] else { return nil }
    return String(raw)
}
