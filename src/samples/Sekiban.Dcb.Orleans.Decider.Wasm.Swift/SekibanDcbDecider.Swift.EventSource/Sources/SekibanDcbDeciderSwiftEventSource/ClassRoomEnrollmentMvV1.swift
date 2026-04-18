import Foundation
import SekibanMv

// Swift port of the C#/Rust `ClassRoomEnrollmentMvV1` projector. Emits the same SQL the other
// ports emit — same DDL, same upsert semantics, same `_last_sortable_unique_id` guard so a
// replay produces the same end state — so the three samples can share Postgres tables and
// registry rows without divergence.

public let classRoomsLogicalTable = "classrooms"
public let studentsLogicalTable = "students"
public let enrollmentsLogicalTable = "enrollments"

public struct ClassRoomEnrollmentMvV1: WasmMvProjector {
    public init() {}

    public let viewName: String = "ClassRoomEnrollment"
    public let viewVersion: Int32 = 1
    public let logicalTables: [String] = [
        classRoomsLogicalTable,
        studentsLogicalTable,
        enrollmentsLogicalTable,
    ]

    public func initialize(tables: MvTableBindingsDto) -> [MvSqlStatementDto] {
        let classrooms = tables.getPhysicalName(classRoomsLogicalTable)
        let students = tables.getPhysicalName(studentsLogicalTable)
        let enrollments = tables.getPhysicalName(enrollmentsLogicalTable)

        return [
            MvSqlStatementDto(sql: """
                CREATE TABLE IF NOT EXISTS \(classrooms) (
                    class_room_id UUID PRIMARY KEY,
                    name TEXT NOT NULL,
                    max_students INT NOT NULL,
                    enrolled_count INT NOT NULL DEFAULT 0,
                    _last_sortable_unique_id TEXT NOT NULL,
                    _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                """),
            MvSqlStatementDto(sql: """
                CREATE TABLE IF NOT EXISTS \(students) (
                    student_id UUID PRIMARY KEY,
                    name TEXT NOT NULL,
                    max_class_count INT NOT NULL,
                    enrolled_count INT NOT NULL DEFAULT 0,
                    _last_sortable_unique_id TEXT NOT NULL,
                    _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                """),
            MvSqlStatementDto(sql: """
                CREATE TABLE IF NOT EXISTS \(enrollments) (
                    student_id UUID NOT NULL,
                    class_room_id UUID NOT NULL,
                    enrolled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    _last_sortable_unique_id TEXT NOT NULL,
                    PRIMARY KEY (student_id, class_room_id)
                );
                """),
            MvSqlStatementDto(sql: """
                CREATE INDEX IF NOT EXISTS \(buildIndexName(enrollments, "class_room")) \
                ON \(enrollments) (class_room_id);
                """),
        ]
    }

    public func applyEvent(
        tables: MvTableBindingsDto,
        event: MvSerializableEventDto,
        queryPort: MvQueryPort
    ) -> [MvSqlStatementDto] {
        _ = queryPort
        guard let data = event.payloadJson.data(using: .utf8) else { return [] }
        switch event.eventType {
        case "ClassRoomCreated":
            guard let created = try? JSONDecoder().decode(ClassRoomCreated.self, from: data) else {
                return []
            }
            return [insertClassRoom(tables: tables, created: created,
                                    sortableUniqueId: event.sortableUniqueId)]
        case "StudentCreated":
            guard let created = try? JSONDecoder().decode(StudentCreated.self, from: data) else {
                return []
            }
            return [insertStudent(tables: tables, created: created,
                                  sortableUniqueId: event.sortableUniqueId)]
        case "StudentEnrolledInClassRoom":
            guard let enrolled = try? JSONDecoder().decode(StudentEnrolledInClassRoom.self, from: data) else {
                return []
            }
            return insertEnrollment(tables: tables, enrolled: enrolled,
                                    sortableUniqueId: event.sortableUniqueId)
        case "StudentDroppedFromClassRoom":
            guard let dropped = try? JSONDecoder().decode(StudentDroppedFromClassRoom.self, from: data) else {
                return []
            }
            return deleteEnrollment(tables: tables, dropped: dropped,
                                    sortableUniqueId: event.sortableUniqueId)
        default:
            return []
        }
    }

    private func insertClassRoom(
        tables: MvTableBindingsDto,
        created: ClassRoomCreated,
        sortableUniqueId: String
    ) -> MvSqlStatementDto {
        let table = tables.getPhysicalName(classRoomsLogicalTable)
        return MvSqlStatementDto(
            sql: """
                INSERT INTO \(table)
                (class_room_id, name, max_students, enrolled_count, _last_sortable_unique_id, _last_applied_at)
                VALUES (@ClassRoomId, @Name, @MaxStudents, 0, @SortableUniqueId, NOW())
                ON CONFLICT (class_room_id) DO UPDATE SET
                name = EXCLUDED.name,
                max_students = EXCLUDED.max_students,
                _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                _last_applied_at = EXCLUDED._last_applied_at
                WHERE \(table)._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
                """,
            parameters: MvParamBuilder()
                .guid("ClassRoomId", created.classRoomId)
                .string("Name", created.name)
                .int32("MaxStudents", created.maxStudents)
                .string("SortableUniqueId", sortableUniqueId)
                .build())
    }

    private func insertStudent(
        tables: MvTableBindingsDto,
        created: StudentCreated,
        sortableUniqueId: String
    ) -> MvSqlStatementDto {
        let table = tables.getPhysicalName(studentsLogicalTable)
        return MvSqlStatementDto(
            sql: """
                INSERT INTO \(table)
                (student_id, name, max_class_count, enrolled_count, _last_sortable_unique_id, _last_applied_at)
                VALUES (@StudentId, @Name, @MaxClassCount, 0, @SortableUniqueId, NOW())
                ON CONFLICT (student_id) DO UPDATE SET
                name = EXCLUDED.name,
                max_class_count = EXCLUDED.max_class_count,
                _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                _last_applied_at = EXCLUDED._last_applied_at
                WHERE \(table)._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
                """,
            parameters: MvParamBuilder()
                .guid("StudentId", created.studentId)
                .string("Name", created.name)
                .int32("MaxClassCount", created.maxClassCount)
                .string("SortableUniqueId", sortableUniqueId)
                .build())
    }

    private func insertEnrollment(
        tables: MvTableBindingsDto,
        enrolled: StudentEnrolledInClassRoom,
        sortableUniqueId: String
    ) -> [MvSqlStatementDto] {
        let enrollments = tables.getPhysicalName(enrollmentsLogicalTable)
        return [
            MvSqlStatementDto(
                sql: """
                    INSERT INTO \(enrollments)
                    (student_id, class_room_id, enrolled_at, _last_sortable_unique_id)
                    VALUES (@StudentId, @ClassRoomId, NOW(), @SortableUniqueId)
                    ON CONFLICT (student_id, class_room_id) DO UPDATE SET
                    _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id
                    WHERE \(enrollments)._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
                    """,
                parameters: MvParamBuilder()
                    .guid("StudentId", enrolled.studentId)
                    .guid("ClassRoomId", enrolled.classRoomId)
                    .string("SortableUniqueId", sortableUniqueId)
                    .build()),
            recountClassRoom(tables: tables, classRoomId: enrolled.classRoomId,
                             sortableUniqueId: sortableUniqueId),
            recountStudent(tables: tables, studentId: enrolled.studentId,
                           sortableUniqueId: sortableUniqueId),
        ]
    }

    private func deleteEnrollment(
        tables: MvTableBindingsDto,
        dropped: StudentDroppedFromClassRoom,
        sortableUniqueId: String
    ) -> [MvSqlStatementDto] {
        let enrollments = tables.getPhysicalName(enrollmentsLogicalTable)
        return [
            MvSqlStatementDto(
                sql: """
                    DELETE FROM \(enrollments)
                    WHERE student_id = @StudentId
                    AND class_room_id = @ClassRoomId
                    AND _last_sortable_unique_id < @SortableUniqueId;
                    """,
                parameters: MvParamBuilder()
                    .guid("StudentId", dropped.studentId)
                    .guid("ClassRoomId", dropped.classRoomId)
                    .string("SortableUniqueId", sortableUniqueId)
                    .build()),
            recountClassRoom(tables: tables, classRoomId: dropped.classRoomId,
                             sortableUniqueId: sortableUniqueId),
            recountStudent(tables: tables, studentId: dropped.studentId,
                           sortableUniqueId: sortableUniqueId),
        ]
    }

    private func recountClassRoom(
        tables: MvTableBindingsDto,
        classRoomId: UUID,
        sortableUniqueId: String
    ) -> MvSqlStatementDto {
        let classrooms = tables.getPhysicalName(classRoomsLogicalTable)
        let enrollments = tables.getPhysicalName(enrollmentsLogicalTable)
        return MvSqlStatementDto(
            sql: """
                UPDATE \(classrooms)
                SET enrolled_count = (
                    SELECT COUNT(*) FROM \(enrollments) WHERE class_room_id = @ClassRoomId
                ),
                _last_sortable_unique_id = @SortableUniqueId,
                _last_applied_at = NOW()
                WHERE class_room_id = @ClassRoomId
                AND _last_sortable_unique_id < @SortableUniqueId;
                """,
            parameters: MvParamBuilder()
                .guid("ClassRoomId", classRoomId)
                .string("SortableUniqueId", sortableUniqueId)
                .build())
    }

    private func recountStudent(
        tables: MvTableBindingsDto,
        studentId: UUID,
        sortableUniqueId: String
    ) -> MvSqlStatementDto {
        let students = tables.getPhysicalName(studentsLogicalTable)
        let enrollments = tables.getPhysicalName(enrollmentsLogicalTable)
        return MvSqlStatementDto(
            sql: """
                UPDATE \(students)
                SET enrolled_count = (
                    SELECT COUNT(*) FROM \(enrollments) WHERE student_id = @StudentId
                ),
                _last_sortable_unique_id = @SortableUniqueId,
                _last_applied_at = NOW()
                WHERE student_id = @StudentId
                AND _last_sortable_unique_id < @SortableUniqueId;
                """,
            parameters: MvParamBuilder()
                .guid("StudentId", studentId)
                .string("SortableUniqueId", sortableUniqueId)
                .build())
    }

    // PostgreSQL identifier length limit is 63 bytes. Mirrors the Rust/C# helper so the port
    // produces identical index names for the common case (physical table name short enough that
    // `idx_{table}_{suffix}` fits). Long physical names get truncated + an SHA-1 prefix.
    private func buildIndexName(_ physicalTable: String, _ suffix: String) -> String {
        let maxLength = 63
        let prefix = "idx_"
        let tail = "_\(suffix)"
        let available = maxLength - prefix.count - tail.count
        if physicalTable.count <= available {
            return "\(prefix)\(physicalTable)\(tail)"
        }
        let hashBytes = sha1(Array(physicalTable.utf8))
        let hashHex = hashBytes.map { String(format: "%02x", $0) }.joined().prefix(8)
        let headroom = max(1, available - 9)
        let head = String(physicalTable.prefix(headroom))
        return "\(prefix)\(head)_\(hashHex)\(tail)"
    }

    /// Hand-rolled SHA-1 — avoids relying on CryptoKit availability under the Wasm SDK.
    /// RFC 3174 reference implementation transcribed to Swift.
    private func sha1(_ message: [UInt8]) -> [UInt8] {
        var h0: UInt32 = 0x67452301
        var h1: UInt32 = 0xEFCDAB89
        var h2: UInt32 = 0x98BADCFE
        var h3: UInt32 = 0x10325476
        var h4: UInt32 = 0xC3D2E1F0

        let ml = UInt64(message.count) * 8
        var padded = message
        padded.append(0x80)
        while padded.count % 64 != 56 { padded.append(0x00) }
        for i in (0..<8).reversed() {
            padded.append(UInt8((ml >> (UInt64(i) * 8)) & 0xFF))
        }

        for chunkStart in stride(from: 0, to: padded.count, by: 64) {
            var w = [UInt32](repeating: 0, count: 80)
            for j in 0..<16 {
                let b0 = UInt32(padded[chunkStart + j * 4])
                let b1 = UInt32(padded[chunkStart + j * 4 + 1])
                let b2 = UInt32(padded[chunkStart + j * 4 + 2])
                let b3 = UInt32(padded[chunkStart + j * 4 + 3])
                w[j] = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3
            }
            for j in 16..<80 {
                let v = w[j - 3] ^ w[j - 8] ^ w[j - 14] ^ w[j - 16]
                w[j] = (v << 1) | (v >> 31)
            }
            var a = h0, b = h1, c = h2, d = h3, e = h4
            for j in 0..<80 {
                let f: UInt32
                let k: UInt32
                switch j {
                case 0..<20:
                    f = (b & c) | ((~b) & d); k = 0x5A827999
                case 20..<40:
                    f = b ^ c ^ d; k = 0x6ED9EBA1
                case 40..<60:
                    f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC
                default:
                    f = b ^ c ^ d; k = 0xCA62C1D6
                }
                let temp = ((a << 5) | (a >> 27)) &+ f &+ e &+ k &+ w[j]
                e = d
                d = c
                c = (b << 30) | (b >> 2)
                b = a
                a = temp
            }
            h0 = h0 &+ a
            h1 = h1 &+ b
            h2 = h2 &+ c
            h3 = h3 &+ d
            h4 = h4 &+ e
        }

        var result: [UInt8] = []
        for h in [h0, h1, h2, h3, h4] {
            result.append(UInt8((h >> 24) & 0xFF))
            result.append(UInt8((h >> 16) & 0xFF))
            result.append(UInt8((h >> 8) & 0xFF))
            result.append(UInt8(h & 0xFF))
        }
        return result
    }
}
