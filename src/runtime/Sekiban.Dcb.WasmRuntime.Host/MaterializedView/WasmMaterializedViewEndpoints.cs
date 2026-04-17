using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
///     Shared read-only <c>/api/mv/*</c> endpoints for the WASM materialized view runtime. Hosted
///     on the WASM runtime process itself (the same service that owns the MV Postgres connection
///     and <see cref="IMvRegistryStore"/>) so every language sample — C# WASM, Rust WASM, any
///     future host — gets the same introspection surface for free.
///
///     <para>Endpoints are registered only when the MV runtime was wired (i.e.
///     <see cref="WasmMaterializedViewExtensions.AddSekibanWasmMaterializedViewRuntime"/>
///     returned <c>true</c>) — call <see cref="MapSekibanWasmMaterializedViewEndpoints"/>
///     after <c>Build()</c>.</para>
///
///     <para>Currently hardcodes the ClassRoomEnrollment view schema (class rooms / students /
///     enrollments) because that is the only view the samples ship. When additional views arrive
///     the endpoints will need to become either projector-schema-aware or generic row-dump
///     endpoints that return JSON shaped from <c>SELECT *</c>.</para>
/// </summary>
public static class WasmMaterializedViewEndpoints
{
    private const string OpenApiTag = "Materialized View";
    private const string ClassRoomsLogical = "classrooms";
    private const string StudentsLogical = "students";
    private const string EnrollmentsLogical = "enrollments";
    private const string ClassRoomEnrollmentView = "ClassRoomEnrollment";
    private const int ClassRoomEnrollmentVersion = 1;

    public static IEndpointRouteBuilder MapSekibanWasmMaterializedViewEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("/api/mv").WithTags(OpenApiTag);
        group.MapGet("/classrooms", GetClassRoomsAsync).WithName("GetWasmMvClassRooms");
        group.MapGet("/students", GetStudentsAsync).WithName("GetWasmMvStudents");
        group.MapGet("/enrollments", GetEnrollmentsAsync).WithName("GetWasmMvEnrollments");
        group.MapGet("/status", GetStatusAsync).WithName("GetWasmMvStatus");
        return endpoints;
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
