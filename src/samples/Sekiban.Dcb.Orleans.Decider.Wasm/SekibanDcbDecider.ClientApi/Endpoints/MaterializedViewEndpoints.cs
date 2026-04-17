using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.ServiceId;

namespace SekibanDcbDecider.ClientApi.Endpoints;

/// <summary>
///     Read-only endpoints backed by the PostgreSQL materialized view. ClientApi does NOT host
///     the MV catch-up runtime — that lives inside <c>Sekiban.Dcb.WasmRuntime.Host</c> via
///     <c>MaterializedViewGrain</c> — so this file only resolves logical→physical table names
///     through <see cref="IMvRegistryStore"/> and runs SELECT queries via Dapper.
/// </summary>
public static class MaterializedViewEndpoints
{
    private const string OpenApiTag = "Materialized View";
    private const string ClassRoomsLogical = "classrooms";
    private const string StudentsLogical = "students";
    private const string EnrollmentsLogical = "enrollments";
    private const string ClassRoomEnrollmentView = "ClassRoomEnrollment";
    private const int ClassRoomEnrollmentVersion = 1;

    public static void MapMaterializedViewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/mv").WithTags(OpenApiTag);
        group.MapGet("/classrooms", GetClassRoomsAsync).WithName("GetMvClassRooms");
        group.MapGet("/students", GetStudentsAsync).WithName("GetMvStudents");
        group.MapGet("/enrollments", GetEnrollmentsAsync).WithName("GetMvEnrollments");
        group.MapGet("/status", GetStatusAsync).WithName("GetMvStatus");
    }

    private static async Task<IResult> GetClassRoomsAsync(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] IMvRegistryStore registryStore,
        [FromServices] IMvStorageInfoProvider storageInfoProvider,
        [FromServices] IServiceIdProvider serviceIdProvider)
    {
        var (limit, offset) = ResolvePaging(pageNumber, pageSize);
        var ctx = await GetViewContextAsync(registryStore, storageInfoProvider, serviceIdProvider);
        var tableName = ctx.GetRequiredTable(ClassRoomsLogical);

        await using var connection = new NpgsqlConnection(ctx.ConnectionString);
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<ClassRoomMvRow>(
            $"""
             SELECT class_room_id AS ClassRoomId, name AS Name, max_students AS MaxStudents,
                    enrolled_count AS EnrolledCount, _last_sortable_unique_id AS LastSortableUniqueId,
                    _last_applied_at AS LastAppliedAt
             FROM {tableName}
             ORDER BY name
             LIMIT @Limit OFFSET @Offset;
             """,
            new { Limit = limit, Offset = offset });
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetStudentsAsync(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] IMvRegistryStore registryStore,
        [FromServices] IMvStorageInfoProvider storageInfoProvider,
        [FromServices] IServiceIdProvider serviceIdProvider)
    {
        var (limit, offset) = ResolvePaging(pageNumber, pageSize);
        var ctx = await GetViewContextAsync(registryStore, storageInfoProvider, serviceIdProvider);
        var tableName = ctx.GetRequiredTable(StudentsLogical);

        await using var connection = new NpgsqlConnection(ctx.ConnectionString);
        await connection.OpenAsync();
        var rows = await connection.QueryAsync<StudentMvRow>(
            $"""
             SELECT student_id AS StudentId, name AS Name, max_class_count AS MaxClassCount,
                    enrolled_count AS EnrolledCount, _last_sortable_unique_id AS LastSortableUniqueId,
                    _last_applied_at AS LastAppliedAt
             FROM {tableName}
             ORDER BY name
             LIMIT @Limit OFFSET @Offset;
             """,
            new { Limit = limit, Offset = offset });
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetEnrollmentsAsync(
        [FromQuery] Guid? classRoomId,
        [FromQuery] Guid? studentId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] IMvRegistryStore registryStore,
        [FromServices] IMvStorageInfoProvider storageInfoProvider,
        [FromServices] IServiceIdProvider serviceIdProvider)
    {
        var ctx = await GetViewContextAsync(registryStore, storageInfoProvider, serviceIdProvider);
        var tableName = ctx.GetRequiredTable(EnrollmentsLogical);

        await using var connection = new NpgsqlConnection(ctx.ConnectionString);
        await connection.OpenAsync();
        var sql =
            $"SELECT student_id AS StudentId, class_room_id AS ClassRoomId, enrolled_at AS EnrolledAt, " +
            $"_last_sortable_unique_id AS LastSortableUniqueId FROM {tableName}";
        var filters = new List<string>();
        var (limit, offset) = ResolvePaging(pageNumber, pageSize);
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit);
        parameters.Add("Offset", offset);
        if (classRoomId is { } cid) { filters.Add("class_room_id = @ClassRoomId"); parameters.Add("ClassRoomId", cid); }
        if (studentId is { } sid) { filters.Add("student_id = @StudentId"); parameters.Add("StudentId", sid); }
        if (filters.Count > 0) sql += " WHERE " + string.Join(" AND ", filters);
        // Paging keeps unbounded enrollment tables from blowing up memory / response time.
        sql += " ORDER BY enrolled_at DESC LIMIT @Limit OFFSET @Offset";

        var rows = await connection.QueryAsync<EnrollmentMvRow>(sql, parameters);
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetStatusAsync(
        [FromServices] IMvRegistryStore registryStore,
        [FromServices] IMvStorageInfoProvider storageInfoProvider,
        [FromServices] IServiceIdProvider serviceIdProvider)
    {
        var ctx = await GetViewContextAsync(registryStore, storageInfoProvider, serviceIdProvider);
        return Results.Ok(new
        {
            ctx.ServiceId,
            databaseType = ctx.DatabaseType,
            entries = ctx.Entries
        });
    }

    private static async Task<MvReadContext> GetViewContextAsync(
        IMvRegistryStore registryStore,
        IMvStorageInfoProvider storageInfoProvider,
        IServiceIdProvider serviceIdProvider)
    {
        var serviceId = serviceIdProvider.GetCurrentServiceId();
        var entries = await registryStore.GetEntriesAsync(serviceId, ClassRoomEnrollmentView, ClassRoomEnrollmentVersion);
        var storageInfo = storageInfoProvider.GetStorageInfo();
        return new MvReadContext(serviceId, storageInfo.DatabaseType, storageInfo.ConnectionString, entries);
    }

    private static (int Limit, int Offset) ResolvePaging(int? pageNumber, int? pageSize)
    {
        var size = pageSize is > 0 ? pageSize.Value : 20;
        var page = pageNumber is > 0 ? pageNumber.Value : 1;
        return (size, (page - 1) * size);
    }

    private sealed record MvReadContext(
        string ServiceId,
        MvDbType DatabaseType,
        string ConnectionString,
        IReadOnlyList<MvRegistryEntry> Entries)
    {
        public string GetRequiredTable(string logicalName)
        {
            var entry = Entries.FirstOrDefault(e => string.Equals(e.LogicalTable, logicalName, StringComparison.Ordinal));
            return entry is null
                ? throw new InvalidOperationException($"Materialized view table '{logicalName}' is not registered.")
                : entry.PhysicalTable;
        }
    }

    public sealed class ClassRoomMvRow
    {
        public Guid ClassRoomId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MaxStudents { get; set; }
        public int EnrolledCount { get; set; }
        public string LastSortableUniqueId { get; set; } = string.Empty;
        public DateTimeOffset LastAppliedAt { get; set; }
    }

    public sealed class StudentMvRow
    {
        public Guid StudentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MaxClassCount { get; set; }
        public int EnrolledCount { get; set; }
        public string LastSortableUniqueId { get; set; } = string.Empty;
        public DateTimeOffset LastAppliedAt { get; set; }
    }

    public sealed class EnrollmentMvRow
    {
        public Guid StudentId { get; set; }
        public Guid ClassRoomId { get; set; }
        public DateTimeOffset EnrolledAt { get; set; }
        public string LastSortableUniqueId { get; set; } = string.Empty;
    }
}
