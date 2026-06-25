using System.Text.Json;
using PublicContainerCsDecider.Domain;
using PublicContainerCsDecider.Domain.Weather;

namespace PublicContainerCsDecider.Wasm.MaterializedView;

/// <summary>
/// A minimal single-table materialized view of WeatherForecast aggregates. It emits the CREATE
/// TABLE DDL (Initialize) and turns each committed WeatherForecast event into an idempotent
/// INSERT/UPDATE (ApplyEvent). The host executes the returned SQL against
/// <c>DcbMaterializedViewPostgres</c>, and a caller-owned verifier reads the resulting row back.
///
/// Idempotency: every write is guarded by <c>_last_sortable_unique_id &lt; @SortableUniqueId</c>
/// so re-delivered/out-of-order events never clobber a newer row.
/// </summary>
public sealed class WeatherForecastMvV1 : IWasmMvProjector
{
    public const string WeatherForecastLogicalTable = "weather_forecast";

    public string ViewName => "WeatherForecast";
    public int ViewVersion => 1;
    public IReadOnlyList<string> LogicalTables => [WeatherForecastLogicalTable];

    public IReadOnlyList<MvSqlStatementDto> Initialize(MvTableBindingsDto tables)
    {
        var table = tables.GetPhysicalName(WeatherForecastLogicalTable);
        return
        [
            new MvSqlStatementDto
            {
                Sql = $"""
                    CREATE TABLE IF NOT EXISTS {table} (
                        forecast_id TEXT PRIMARY KEY,
                        location TEXT NOT NULL,
                        temperature_c INT NOT NULL,
                        summary TEXT NULL,
                        created_at TIMESTAMPTZ NULL,
                        is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                        _last_sortable_unique_id TEXT NOT NULL,
                        _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );
                    """
            }
        ];
    }

    public IReadOnlyList<MvSqlStatementDto> ApplyEvent(
        MvTableBindingsDto tables,
        MvSerializableEventDto serializableEvent,
        IWasmMvQueryPort queryPort)
    {
        var table = tables.GetPhysicalName(WeatherForecastLogicalTable);
        return serializableEvent.EventType switch
        {
            nameof(WeatherForecastCreated) =>
            [
                Insert(
                    table,
                    Deserialize(serializableEvent.PayloadJson, DomainJsonContext.Default.WeatherForecastCreated),
                    serializableEvent.SortableUniqueId)
            ],
            nameof(WeatherForecastLocationUpdated) =>
            [
                UpdateLocation(
                    table,
                    Deserialize(serializableEvent.PayloadJson, DomainJsonContext.Default.WeatherForecastLocationUpdated),
                    serializableEvent.SortableUniqueId)
            ],
            nameof(WeatherForecastDeleted) =>
            [
                SoftDelete(
                    table,
                    Deserialize(serializableEvent.PayloadJson, DomainJsonContext.Default.WeatherForecastDeleted),
                    serializableEvent.SortableUniqueId)
            ],
            _ => []
        };
    }

    private static MvSqlStatementDto Insert(string table, WeatherForecastCreated e, string sortableUniqueId) =>
        new()
        {
            Sql = $"""
                INSERT INTO {table}
                    (forecast_id, location, temperature_c, summary, created_at, is_deleted,
                     _last_sortable_unique_id, _last_applied_at)
                VALUES
                    (@ForecastId, @Location, @TemperatureC, @Summary, @CreatedAt, FALSE,
                     @SortableUniqueId, NOW())
                ON CONFLICT (forecast_id) DO UPDATE SET
                    location = EXCLUDED.location,
                    temperature_c = EXCLUDED.temperature_c,
                    summary = EXCLUDED.summary,
                    created_at = EXCLUDED.created_at,
                    is_deleted = FALSE,
                    _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                    _last_applied_at = NOW()
                WHERE {table}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
                """,
            Parameters = new MvParamBuilder()
                .String("ForecastId", e.ForecastId)
                .String("Location", e.Location)
                .Int32("TemperatureC", e.TemperatureC)
                .String("Summary", e.Summary)
                .DateTimeOffset("CreatedAt", e.CreatedAt)
                .String("SortableUniqueId", sortableUniqueId)
                .Build()
        };

    private static MvSqlStatementDto UpdateLocation(string table, WeatherForecastLocationUpdated e, string sortableUniqueId) =>
        new()
        {
            Sql = $"""
                UPDATE {table}
                SET location = @NewLocation,
                    _last_sortable_unique_id = @SortableUniqueId,
                    _last_applied_at = NOW()
                WHERE forecast_id = @ForecastId
                  AND _last_sortable_unique_id < @SortableUniqueId;
                """,
            Parameters = new MvParamBuilder()
                .String("ForecastId", e.ForecastId)
                .String("NewLocation", e.NewLocation)
                .String("SortableUniqueId", sortableUniqueId)
                .Build()
        };

    private static MvSqlStatementDto SoftDelete(string table, WeatherForecastDeleted e, string sortableUniqueId) =>
        new()
        {
            Sql = $"""
                UPDATE {table}
                SET is_deleted = TRUE,
                    _last_sortable_unique_id = @SortableUniqueId,
                    _last_applied_at = NOW()
                WHERE forecast_id = @ForecastId
                  AND _last_sortable_unique_id < @SortableUniqueId;
                """,
            Parameters = new MvParamBuilder()
                .String("ForecastId", e.ForecastId)
                .String("SortableUniqueId", sortableUniqueId)
                .Build()
        };

    private static T Deserialize<T>(string payloadJson, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(payloadJson, typeInfo)
            ?? throw new InvalidOperationException($"Empty payload for {typeof(T).Name}.");
}
