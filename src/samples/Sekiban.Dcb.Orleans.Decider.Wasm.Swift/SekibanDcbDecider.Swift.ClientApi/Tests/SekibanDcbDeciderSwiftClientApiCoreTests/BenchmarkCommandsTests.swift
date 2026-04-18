import XCTest
@testable import SekibanDcbDeciderSwiftClientApiCore

final class BenchmarkCommandsTests: XCTestCase {
    private let fixedNow: Date = {
        var components = DateComponents()
        components.year = 2026
        components.month = 4
        components.day = 18
        components.hour = 12
        components.minute = 0
        components.second = 0
        components.nanosecond = 500_000_000
        components.timeZone = TimeZone(identifier: "UTC")
        return Calendar(identifier: .iso8601).date(from: components)!
    }()

    private func makeUUID(_ seed: String) -> UUID {
        // Deterministic UUID: take SHA-ish of seed. UUID() is non-deterministic; for tests
        // we need stable values to compare the base64-encoded event payload.
        var bytes = Array(seed.utf8)
        while bytes.count < 16 { bytes.append(0) }
        bytes = Array(bytes.prefix(16))
        bytes[6] = (bytes[6] & 0x0F) | 0x40 // RFC 4122 version 4
        bytes[8] = (bytes[8] & 0x3F) | 0x80 // RFC 4122 variant
        let uuid = (
            bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
            bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15])
        return UUID(uuid: uuid)
    }

    func testBuildCreateRoomCommit_ProducesOneRoomCreatedEvent() throws {
        let roomId = makeUUID("room-001")
        let request = CreateRoomRequest(
            roomId: roomId,
            name: "Benchmark Room 01",
            capacity: 8,
            location: "Building A, Floor 1",
            equipment: ["Projector", "Whiteboard"],
            requiresApproval: false)

        let result = try buildCreateRoomCommit(request: request, now: { self.fixedNow })

        XCTAssertEqual(result.roomId, roomId)
        XCTAssertEqual(result.eventTypeName, "RoomCreated")
        XCTAssertEqual(result.request.eventCandidates.count, 1)
        XCTAssertEqual(result.request.consistencyTags.count, 1)

        let candidate = result.request.eventCandidates[0]
        XCTAssertEqual(candidate.eventPayloadName, "RoomCreated")
        XCTAssertEqual(candidate.tags, ["Room:\(roomId.lowercasedUUID)"])

        // Decode the base64 payload back to JSON and verify the field shape matches Rust's
        // event output (camelCase, ISO8601 createdAt with fractional seconds).
        let payloadData = Data(base64Encoded: candidate.payload)
        XCTAssertNotNil(payloadData, "payload must be valid base64")
        let event = try JSONDecoder().decode(RoomCreated.self, from: payloadData!)
        XCTAssertEqual(event.roomId, roomId)
        XCTAssertEqual(event.name, "Benchmark Room 01")
        XCTAssertEqual(event.capacity, 8)
        XCTAssertEqual(event.location, "Building A, Floor 1")
        XCTAssertEqual(event.equipment, ["Projector", "Whiteboard"])
        XCTAssertFalse(event.requiresApproval)
        XCTAssertEqual(event.createdAt, "2026-04-18T12:00:00.500Z")

        XCTAssertEqual(result.request.consistencyTags[0].tag, "Room:\(roomId.lowercasedUUID)")
        XCTAssertEqual(result.request.consistencyTags[0].lastSortableUniqueId, "")
    }

    func testBuildCreateRoomCommit_MintsIdWhenAbsent() throws {
        let request = CreateRoomRequest(
            roomId: nil,
            name: "Auto Room",
            capacity: 4,
            location: "Lab",
            equipment: ["Whiteboard"],
            requiresApproval: false)

        let result = try buildCreateRoomCommit(request: request, now: { self.fixedNow })
        // Not nil / not all-zero — UUID() produces a fresh value.
        XCTAssertNotEqual(result.roomId, UUID(uuid: (0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0)))
        XCTAssertEqual(result.request.consistencyTags[0].tag,
                       "Room:\(result.roomId.lowercasedUUID)")
    }

    func testBuildCreateWeatherForecastCommit_ProducesOneEvent() throws {
        let forecastId = makeUUID("forecast-001")
        let request = CreateWeatherForecastRequest(
            forecastId: forecastId,
            location: "City-000042",
            date: "2026-05-03",
            temperatureC: 22,
            summary: "Warm")

        let result = try buildCreateWeatherForecastCommit(request: request, now: { self.fixedNow })

        XCTAssertEqual(result.forecastId, forecastId)
        XCTAssertEqual(result.request.eventCandidates.count, 1)
        let candidate = result.request.eventCandidates[0]
        XCTAssertEqual(candidate.eventPayloadName, "WeatherForecastCreated")
        XCTAssertEqual(candidate.tags, ["weather:\(forecastId.lowercasedUUID)"])

        let event = try JSONDecoder().decode(
            WeatherForecastCreated.self,
            from: Data(base64Encoded: candidate.payload)!)
        XCTAssertEqual(event.forecastId, forecastId)
        XCTAssertEqual(event.location, "City-000042")
        XCTAssertEqual(event.date, "2026-05-03")
        XCTAssertEqual(event.temperatureC, 22)
        XCTAssertEqual(event.summary, "Warm")
        XCTAssertEqual(event.createdAt, "2026-04-18T12:00:00.500Z")
    }

    func testBuildCreateQuickReservationCommit_ThreeEventsOnTwoTags() throws {
        let reservationId = makeUUID("reservation-001")
        let roomId = makeUUID("room-007")
        let organizerId = makeUUID("user-123")
        let request = CreateQuickReservationRequest(
            reservationId: reservationId,
            roomId: roomId,
            organizerId: organizerId,
            organizerName: "Benchmark User",
            startTime: "2026-05-03T09:00:00Z",
            endTime: "2026-05-03T10:00:00Z",
            attendeeCount: 4,
            purpose: "Benchmark meeting",
            selectedEquipment: [])

        // Synchronous fast-path builder — matches the Rust handler's event fan-out on
        // `Reservation:<reservationId>` + `RoomReservation:<roomId>` without the
        // round-trip I/O (exercised by the reader-backed builder in integration tests).
        let result = try buildCreateQuickReservationCommit(request: request, now: { self.fixedNow })

        XCTAssertEqual(result.reservationId, reservationId)
        XCTAssertEqual(result.request.eventCandidates.count, 3)
        XCTAssertEqual(result.request.consistencyTags.count, 2)
        let reservationTag = "Reservation:\(reservationId.lowercasedUUID)"
        let roomReservationTag = "RoomReservation:\(roomId.lowercasedUUID)"
        XCTAssertEqual(
            Set(result.request.consistencyTags.map(\.tag)),
            Set([reservationTag, roomReservationTag]))

        let eventTypes = result.request.eventCandidates.map(\.eventPayloadName)
        XCTAssertEqual(eventTypes, [
            "ReservationDraftCreated",
            "ReservationHoldCommitted",
            "ReservationConfirmed",
        ])

        // Every candidate should carry BOTH reservation tags (the fan-out that matches
        // Rust's `multi_event_output(..., [reservation_tag, room_reservation_tag], ...)`).
        for candidate in result.request.eventCandidates {
            XCTAssertEqual(Set(candidate.tags), Set([reservationTag, roomReservationTag]))
        }

        // Draft event round-trip check — the most field-heavy of the three.
        let draftData = Data(base64Encoded: result.request.eventCandidates[0].payload)!
        let draft = try JSONDecoder().decode(ReservationDraftCreated.self, from: draftData)
        XCTAssertEqual(draft.reservationId, reservationId)
        XCTAssertEqual(draft.roomId, roomId)
        XCTAssertEqual(draft.organizerId, organizerId)
        XCTAssertEqual(draft.organizerName, "Benchmark User")
        XCTAssertEqual(draft.purpose, "Benchmark meeting")
        XCTAssertEqual(draft.selectedEquipment, [])
    }

    func testCommitRequestRoundTripsThroughJSON() throws {
        let roomId = makeUUID("room-xyz")
        let result = try buildCreateRoomCommit(
            request: CreateRoomRequest(
                roomId: roomId,
                name: "Name",
                capacity: 1,
                location: "Here",
                equipment: [],
                requiresApproval: true),
            now: { self.fixedNow })
        let encoder = makeDefaultCommitJSONEncoder()
        let data = try encoder.encode(result.request)
        let decoded = try JSONDecoder().decode(SerializableCommitRequest.self, from: data)
        XCTAssertEqual(decoded, result.request)
    }

    func testSekibanTimeEmitsFractionalSeconds() {
        let encoded = SekibanTime.iso8601(from: fixedNow)
        // Expect `2026-04-18T12:00:00.500Z` — the fractional-seconds part matches the Rust
        // `Utc::now().to_rfc3339()` shape (3-digit millisecond fraction).
        XCTAssertTrue(encoded.hasPrefix("2026-04-18T12:00:00"), "got \(encoded)")
        XCTAssertTrue(encoded.contains("."), "fractional seconds missing: \(encoded)")
        XCTAssertTrue(encoded.hasSuffix("Z"), "UTC suffix missing: \(encoded)")
    }
}
