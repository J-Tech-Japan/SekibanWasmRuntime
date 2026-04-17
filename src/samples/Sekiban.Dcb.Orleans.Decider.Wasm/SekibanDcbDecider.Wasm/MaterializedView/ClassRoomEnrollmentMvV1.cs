using System.Text.Json;
using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.Events.Student;

namespace SekibanDcbDecider.Wasm.MaterializedView;

/// <summary>
/// Materialized view for classrooms, students and enrollments. Mirrors the projector shipped in
/// the Sekiban.Dcb.Orleans.Decider template but runs inside the WASM module so it can
/// pattern-match on CLR event types (<see cref="ClassRoomCreated"/>, etc.) without needing the
/// host to know the domain assembly.
///
/// SQL generation is idempotent via the `_last_sortable_unique_id` guard so replay-after-failure
/// produces the same end state as a clean run.
/// </summary>
public sealed class ClassRoomEnrollmentMvV1 : IWasmMvProjector
{
    public const string ClassRoomsLogicalTable = "classrooms";
    public const string StudentsLogicalTable = "students";
    public const string EnrollmentsLogicalTable = "enrollments";

    public string ViewName => "ClassRoomEnrollment";
    public int ViewVersion => 1;
    public IReadOnlyList<string> LogicalTables => [ClassRoomsLogicalTable, StudentsLogicalTable, EnrollmentsLogicalTable];

    public IReadOnlyList<MvSqlStatementDto> Initialize(MvTableBindingsDto tables)
    {
        var classRooms = tables.GetPhysicalName(ClassRoomsLogicalTable);
        var students = tables.GetPhysicalName(StudentsLogicalTable);
        var enrollments = tables.GetPhysicalName(EnrollmentsLogicalTable);

        return
        [
            new MvSqlStatementDto
            {
                Sql =
                    $"""
                     CREATE TABLE IF NOT EXISTS {classRooms} (
                         class_room_id UUID PRIMARY KEY,
                         name TEXT NOT NULL,
                         max_students INT NOT NULL,
                         enrolled_count INT NOT NULL DEFAULT 0,
                         _last_sortable_unique_id TEXT NOT NULL,
                         _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                     );
                     """
            },
            new MvSqlStatementDto
            {
                Sql =
                    $"""
                     CREATE TABLE IF NOT EXISTS {students} (
                         student_id UUID PRIMARY KEY,
                         name TEXT NOT NULL,
                         max_class_count INT NOT NULL,
                         enrolled_count INT NOT NULL DEFAULT 0,
                         _last_sortable_unique_id TEXT NOT NULL,
                         _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                     );
                     """
            },
            new MvSqlStatementDto
            {
                Sql =
                    $"""
                     CREATE TABLE IF NOT EXISTS {enrollments} (
                         student_id UUID NOT NULL,
                         class_room_id UUID NOT NULL,
                         enrolled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                         _last_sortable_unique_id TEXT NOT NULL,
                         PRIMARY KEY (student_id, class_room_id)
                     );
                     """
            },
            new MvSqlStatementDto
            {
                Sql =
                    $"""
                     CREATE INDEX IF NOT EXISTS {BuildIndexName(enrollments, "class_room")}
                     ON {enrollments} (class_room_id);
                     """
            }
        ];
    }

    public IReadOnlyList<MvSqlStatementDto> ApplyEvent(
        MvTableBindingsDto tables,
        MvSerializableEventDto serializableEvent,
        IWasmMvQueryPort queryPort)
    {
        // Dispatch on the event-type name. The WASM side already has AOT-friendly JsonTypeInfo
        // for each payload via WasmJsonContext (see WasmDispatcher), so we reuse that generated
        // metadata rather than pulling in a CLR reflection-based deserializer.
        return serializableEvent.EventType switch
        {
            nameof(ClassRoomCreated) => [InsertClassRoom(
                tables,
                JsonSerializer.Deserialize(serializableEvent.PayloadJson, WasmJsonContext.Default.ClassRoomCreated)
                    ?? throw BadPayload(nameof(ClassRoomCreated)),
                serializableEvent.SortableUniqueId)],
            nameof(StudentCreated) => [InsertStudent(
                tables,
                JsonSerializer.Deserialize(serializableEvent.PayloadJson, WasmJsonContext.Default.StudentCreated)
                    ?? throw BadPayload(nameof(StudentCreated)),
                serializableEvent.SortableUniqueId)],
            nameof(StudentEnrolledInClassRoom) => InsertEnrollment(
                tables,
                JsonSerializer.Deserialize(serializableEvent.PayloadJson, WasmJsonContext.Default.StudentEnrolledInClassRoom)
                    ?? throw BadPayload(nameof(StudentEnrolledInClassRoom)),
                serializableEvent.SortableUniqueId),
            nameof(StudentDroppedFromClassRoom) => DeleteEnrollment(
                tables,
                JsonSerializer.Deserialize(serializableEvent.PayloadJson, WasmJsonContext.Default.StudentDroppedFromClassRoom)
                    ?? throw BadPayload(nameof(StudentDroppedFromClassRoom)),
                serializableEvent.SortableUniqueId),
            _ => []
        };
    }

    private static InvalidOperationException BadPayload(string typeName) =>
        new($"Failed to deserialize MV event payload as {typeName}.");

    private static MvSqlStatementDto InsertClassRoom(
        MvTableBindingsDto tables,
        ClassRoomCreated created,
        string sortableUniqueId)
    {
        var table = tables.GetPhysicalName(ClassRoomsLogicalTable);
        return new MvSqlStatementDto
        {
            Sql =
                $"""
                 INSERT INTO {table}
                     (class_room_id, name, max_students, enrolled_count, _last_sortable_unique_id, _last_applied_at)
                 VALUES
                     (@ClassRoomId, @Name, @MaxStudents, 0, @SortableUniqueId, NOW())
                 ON CONFLICT (class_room_id) DO UPDATE SET
                     name = EXCLUDED.name,
                     max_students = EXCLUDED.max_students,
                     _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                     _last_applied_at = EXCLUDED._last_applied_at
                 WHERE {table}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
                 """,
            Parameters = new MvParamBuilder()
                .Guid("ClassRoomId", created.ClassRoomId)
                .String("Name", created.Name)
                .Int32("MaxStudents", created.MaxStudents)
                .String("SortableUniqueId", sortableUniqueId)
                .Build()
        };
    }

    private static MvSqlStatementDto InsertStudent(
        MvTableBindingsDto tables,
        StudentCreated created,
        string sortableUniqueId)
    {
        var table = tables.GetPhysicalName(StudentsLogicalTable);
        return new MvSqlStatementDto
        {
            Sql =
                $"""
                 INSERT INTO {table}
                     (student_id, name, max_class_count, enrolled_count, _last_sortable_unique_id, _last_applied_at)
                 VALUES
                     (@StudentId, @Name, @MaxClassCount, 0, @SortableUniqueId, NOW())
                 ON CONFLICT (student_id) DO UPDATE SET
                     name = EXCLUDED.name,
                     max_class_count = EXCLUDED.max_class_count,
                     _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                     _last_applied_at = EXCLUDED._last_applied_at
                 WHERE {table}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
                 """,
            Parameters = new MvParamBuilder()
                .Guid("StudentId", created.StudentId)
                .String("Name", created.Name)
                .Int32("MaxClassCount", created.MaxClassCount)
                .String("SortableUniqueId", sortableUniqueId)
                .Build()
        };
    }

    private static IReadOnlyList<MvSqlStatementDto> InsertEnrollment(
        MvTableBindingsDto tables,
        StudentEnrolledInClassRoom enrolled,
        string sortableUniqueId)
    {
        var enrollments = tables.GetPhysicalName(EnrollmentsLogicalTable);
        return
        [
            new MvSqlStatementDto
            {
                Sql =
                    $"""
                     INSERT INTO {enrollments}
                         (student_id, class_room_id, enrolled_at, _last_sortable_unique_id)
                     VALUES
                         (@StudentId, @ClassRoomId, NOW(), @SortableUniqueId)
                     ON CONFLICT (student_id, class_room_id) DO UPDATE SET
                         _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id
                     WHERE {enrollments}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
                     """,
                Parameters = new MvParamBuilder()
                    .Guid("StudentId", enrolled.StudentId)
                    .Guid("ClassRoomId", enrolled.ClassRoomId)
                    .String("SortableUniqueId", sortableUniqueId)
                    .Build()
            },
            RecountClassRoom(tables, enrolled.ClassRoomId, sortableUniqueId),
            RecountStudent(tables, enrolled.StudentId, sortableUniqueId)
        ];
    }

    private static IReadOnlyList<MvSqlStatementDto> DeleteEnrollment(
        MvTableBindingsDto tables,
        StudentDroppedFromClassRoom dropped,
        string sortableUniqueId)
    {
        var enrollments = tables.GetPhysicalName(EnrollmentsLogicalTable);
        return
        [
            new MvSqlStatementDto
            {
                Sql =
                    $"""
                     DELETE FROM {enrollments}
                     WHERE student_id = @StudentId
                       AND class_room_id = @ClassRoomId
                       AND _last_sortable_unique_id < @SortableUniqueId;
                     """,
                Parameters = new MvParamBuilder()
                    .Guid("StudentId", dropped.StudentId)
                    .Guid("ClassRoomId", dropped.ClassRoomId)
                    .String("SortableUniqueId", sortableUniqueId)
                    .Build()
            },
            RecountClassRoom(tables, dropped.ClassRoomId, sortableUniqueId),
            RecountStudent(tables, dropped.StudentId, sortableUniqueId)
        ];
    }

    private static MvSqlStatementDto RecountClassRoom(MvTableBindingsDto tables, Guid classRoomId, string sortableUniqueId)
    {
        var classRooms = tables.GetPhysicalName(ClassRoomsLogicalTable);
        var enrollments = tables.GetPhysicalName(EnrollmentsLogicalTable);
        return new MvSqlStatementDto
        {
            Sql =
                $"""
                 UPDATE {classRooms}
                 SET enrolled_count = (
                         SELECT COUNT(*) FROM {enrollments}
                         WHERE class_room_id = @ClassRoomId
                     ),
                     _last_sortable_unique_id = @SortableUniqueId,
                     _last_applied_at = NOW()
                 WHERE class_room_id = @ClassRoomId
                   AND _last_sortable_unique_id < @SortableUniqueId;
                 """,
            Parameters = new MvParamBuilder()
                .Guid("ClassRoomId", classRoomId)
                .String("SortableUniqueId", sortableUniqueId)
                .Build()
        };
    }

    private static MvSqlStatementDto RecountStudent(MvTableBindingsDto tables, Guid studentId, string sortableUniqueId)
    {
        var students = tables.GetPhysicalName(StudentsLogicalTable);
        var enrollments = tables.GetPhysicalName(EnrollmentsLogicalTable);
        return new MvSqlStatementDto
        {
            Sql =
                $"""
                 UPDATE {students}
                 SET enrolled_count = (
                         SELECT COUNT(*) FROM {enrollments}
                         WHERE student_id = @StudentId
                     ),
                     _last_sortable_unique_id = @SortableUniqueId,
                     _last_applied_at = NOW()
                 WHERE student_id = @StudentId
                   AND _last_sortable_unique_id < @SortableUniqueId;
                 """,
            Parameters = new MvParamBuilder()
                .Guid("StudentId", studentId)
                .String("SortableUniqueId", sortableUniqueId)
                .Build()
        };
    }

    // PostgreSQL identifier length limit is 63 bytes. The MV runtime truncates physical table
    // names, but the naive `idx_{table}_{col}` template can still overflow once the prefix and
    // suffix are added. Build a name that fits by shortening the table portion and appending a
    // short stable hash so it stays unique.
    private static string BuildIndexName(string physicalTable, string suffix)
    {
        const int maxLength = 63;
        const string prefix = "idx_";
        var tail = "_" + suffix;
        var available = maxLength - prefix.Length - tail.Length;

        if (physicalTable.Length <= available)
        {
            return prefix + physicalTable + tail;
        }

        var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA1.HashData(
                    System.Text.Encoding.UTF8.GetBytes(physicalTable)))
            .Substring(0, 8)
            .ToLowerInvariant();
        var headroom = available - 9;
        if (headroom < 1) headroom = 1;
        var head = physicalTable.Substring(0, Math.Min(headroom, physicalTable.Length));
        return prefix + head + "_" + hash + tail;
    }
}
