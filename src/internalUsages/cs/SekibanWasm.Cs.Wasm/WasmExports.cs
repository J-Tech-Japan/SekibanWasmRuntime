using System.Runtime.InteropServices;
using System.Text.Json;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.EquipmentType;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.Events.UserAccess;
using Dcb.MeetingRoomModels.Events.UserDirectory;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest.Deciders;
using Dcb.MeetingRoomModels.States.EquipmentType;
using Dcb.MeetingRoomModels.States.EquipmentType.Deciders;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Reservation.Deciders;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.States.Room.Deciders;
using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.States.UserAccess.Deciders;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.States.UserDirectory.Deciders;
using Dcb.MeetingRoomModels.States.UserMonthlyReservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.Wasm;

public static class WasmExports
{
    private enum ProjectorKind
    {
        Unknown,
        WeatherTag,
        WeatherList,
        RoomTag,
        ReservationTag,
        ApprovalRequestTag,
        RoomReservationsTag,
        RoomDailyActivityTag,
        UserAccessTag,
        UserDirectoryTag,
        UserMonthlyReservationTag,
        EquipmentTypeTag
    }

    private sealed class ProjectorInstanceState
    {
        public ProjectorKind Kind { get; init; }
        public ITagStatePayload TagState { get; set; } = new EmptyTagStatePayload();
        public WeatherForecastMultiProjection WeatherMultiState { get; set; } =
            WeatherForecastMultiProjection.GenerateInitialPayload();
    }

    private static readonly object _gate = new();
    private static readonly Dictionary<int, ProjectorInstanceState> _instances = new();
    private static int _nextInstanceId = 1;

    private static DcbDomainTypes DomainTypes => DomainType.GetWasmDomainTypes();

    [UnmanagedCallersOnly(EntryPoint = "alloc")]
    public static unsafe int Alloc(int size)
    {
        if (size <= 0) return 0;
        var ptr = NativeMemory.Alloc((nuint)size);
        return (int)ptr;
    }

    [UnmanagedCallersOnly(EntryPoint = "dealloc")]
    public static unsafe void Dealloc(int ptr, int size)
    {
        if (ptr == 0) return;
        NativeMemory.Free((void*)ptr);
    }

    [UnmanagedCallersOnly(EntryPoint = "create_instance")]
    public static int CreateInstance(int projectorTypePtr, int projectorTypeLen)
    {
        var projectorType = ReadString(projectorTypePtr, projectorTypeLen);
        var kind = ResolveProjectorKind(projectorType);

        if (kind == ProjectorKind.Unknown)
        {
            return -1;
        }

        var instance = new ProjectorInstanceState
        {
            Kind = kind,
            TagState = GetInitialState(kind),
            WeatherMultiState = WeatherForecastMultiProjection.GenerateInitialPayload()
        };

        lock (_gate)
        {
            var id = _nextInstanceId++;
            _instances[id] = instance;
            return id;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "apply_event")]
    public static void ApplyEvent(
        int instanceId,
        int eventTypePtr, int eventTypeLen,
        int payloadPtr, int payloadLen)
    {
        var instance = GetInstance(instanceId);
        if (instance == null) return;

        var eventType = ReadString(eventTypePtr, eventTypeLen);
        var payloadJson = ReadString(payloadPtr, payloadLen);
        ApplyEventInternal(instance, eventType, payloadJson);
    }

    [UnmanagedCallersOnly(EntryPoint = "apply_events_batch")]
    public static int ApplyEventsBatch(int instanceId, int jsonPtr, int jsonLen)
    {
        var instance = GetInstance(instanceId);
        if (instance == null) return -1;

        var json = ReadString(jsonPtr, jsonLen);
        if (string.IsNullOrWhiteSpace(json)) return 0;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return -1;

            var applied = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) break;
                if (!TryGetStringProperty(item, "eventType", out var eventType) ||
                    !TryGetStringProperty(item, "payloadJson", out var payloadJson))
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(eventType)) break;
                ApplyEventInternal(instance, eventType, payloadJson);
                applied++;
            }
            return applied;
        }
        catch
        {
            return -1;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "execute_query")]
    public static long ExecuteQuery(
        int instanceId,
        int queryTypePtr, int queryTypeLen,
        int paramsPtr, int paramsLen)
    {
        var instance = GetInstance(instanceId);
        if (instance == null) return WriteString("null");

        var queryType = ReadString(queryTypePtr, queryTypeLen);
        var queryParamsJson = ReadString(paramsPtr, paramsLen);

        if (instance.Kind is not ProjectorKind.WeatherList)
        {
            return WriteString("null");
        }

        var result = queryType switch
        {
            "GetWeatherForecastCountQuery" => ExecuteCountQuery(queryParamsJson, instance.WeatherMultiState),
            _ => "null"
        };

        return WriteString(result);
    }

    [UnmanagedCallersOnly(EntryPoint = "execute_list_query")]
    public static long ExecuteListQuery(
        int instanceId,
        int queryTypePtr, int queryTypeLen,
        int paramsPtr, int paramsLen)
    {
        var instance = GetInstance(instanceId);
        if (instance == null) return WriteString("[]");

        var queryType = ReadString(queryTypePtr, queryTypeLen);
        var queryParamsJson = ReadString(paramsPtr, paramsLen);

        if (instance.Kind != ProjectorKind.WeatherList)
        {
            return WriteString("[]");
        }

        var result = queryType switch
        {
            "GetWeatherForecastListQuery" or "WeatherForecastListQuery" =>
                ExecuteListQueryInternal(queryParamsJson, instance.WeatherMultiState),
            _ => "[]"
        };

        return WriteString(result);
    }

    [UnmanagedCallersOnly(EntryPoint = "serialize_state")]
    public static long SerializeState(int instanceId)
    {
        var instance = GetInstance(instanceId);
        if (instance == null) return WriteString("{}");

        var json = instance.Kind switch
        {
            ProjectorKind.WeatherList => JsonSerializer.Serialize(
                instance.WeatherMultiState,
                DomainJsonContext.Default.WeatherForecastMultiProjection),
            ProjectorKind.WeatherTag => SerializeTagState<WeatherForecastState>(
                instance.TagState, DomainJsonContext.Default.WeatherForecastState),
            ProjectorKind.RoomTag => SerializeTagState<RoomState>(
                instance.TagState, DomainJsonContext.Default.RoomState),
            ProjectorKind.ReservationTag => SerializeTagState(
                instance.TagState, DomainJsonContext.Default.ReservationState),
            ProjectorKind.ApprovalRequestTag => SerializeTagState(
                instance.TagState, DomainJsonContext.Default.ApprovalRequestState),
            ProjectorKind.RoomReservationsTag => SerializeTagState<RoomReservationsState>(
                instance.TagState, DomainJsonContext.Default.RoomReservationsState),
            ProjectorKind.RoomDailyActivityTag => SerializeTagState<RoomDailyActivityState>(
                instance.TagState, DomainJsonContext.Default.RoomDailyActivityState),
            ProjectorKind.UserAccessTag => SerializeTagState(
                instance.TagState, DomainJsonContext.Default.UserAccessState),
            ProjectorKind.UserDirectoryTag => SerializeTagState(
                instance.TagState, DomainJsonContext.Default.UserDirectoryState),
            ProjectorKind.UserMonthlyReservationTag => SerializeTagState<UserMonthlyReservationState>(
                instance.TagState, DomainJsonContext.Default.UserMonthlyReservationState),
            ProjectorKind.EquipmentTypeTag => SerializeTagState(
                instance.TagState, DomainJsonContext.Default.EquipmentTypeState),
            _ => "{}"
        };

        return WriteString(json);
    }

    [UnmanagedCallersOnly(EntryPoint = "restore_state")]
    public static void RestoreState(int instanceId, int statePtr, int stateLen)
    {
        var instance = GetInstance(instanceId);
        if (instance == null) return;

        var json = ReadString(statePtr, stateLen);

        switch (instance.Kind)
        {
            case ProjectorKind.WeatherList:
                instance.WeatherMultiState = DeserializeWeatherMultiState(json);
                break;
            case ProjectorKind.WeatherTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.WeatherForecastState);
                break;
            case ProjectorKind.RoomTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.RoomState);
                break;
            case ProjectorKind.ReservationTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.ReservationState);
                break;
            case ProjectorKind.ApprovalRequestTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.ApprovalRequestState);
                break;
            case ProjectorKind.RoomReservationsTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.RoomReservationsState);
                break;
            case ProjectorKind.RoomDailyActivityTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.RoomDailyActivityState);
                break;
            case ProjectorKind.UserAccessTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.UserAccessState);
                break;
            case ProjectorKind.UserDirectoryTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.UserDirectoryState);
                break;
            case ProjectorKind.UserMonthlyReservationTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.UserMonthlyReservationState);
                break;
            case ProjectorKind.EquipmentTypeTag:
                instance.TagState = DeserializeTagState(json,
                    DomainJsonContext.Default.EquipmentTypeState);
                break;
            default:
                instance.TagState = new EmptyTagStatePayload();
                break;
        }
    }

    private static void ApplyEventInternal(ProjectorInstanceState instance, string eventType, string payloadJson)
    {
        var payload = DomainTypes.EventTypes.DeserializeEventPayload(eventType, payloadJson);
        if (payload is null)
        {
            return;
        }

        var ev = CreateEvent(payload, eventType);
        var tags = ExtractTags(payload);

        switch (instance.Kind)
        {
            case ProjectorKind.WeatherList:
                instance.WeatherMultiState = WeatherForecastMultiProjection.Project(
                    instance.WeatherMultiState, ev, tags, DomainTypes,
                    SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()));
                break;
            case ProjectorKind.WeatherTag:
                instance.TagState = WeatherForecastProjector.Project(instance.TagState, ev);
                break;
            case ProjectorKind.RoomTag:
                instance.TagState = ProjectRoom(instance.TagState, ev);
                break;
            case ProjectorKind.ReservationTag:
                instance.TagState = ProjectReservation(instance.TagState, ev);
                break;
            case ProjectorKind.ApprovalRequestTag:
                instance.TagState = ProjectApprovalRequest(instance.TagState, ev);
                break;
            case ProjectorKind.RoomReservationsTag:
                instance.TagState = ProjectRoomReservations(instance.TagState, ev);
                break;
            case ProjectorKind.RoomDailyActivityTag:
                instance.TagState = ProjectRoomDailyActivity(instance.TagState, ev);
                break;
            case ProjectorKind.UserAccessTag:
                instance.TagState = ProjectUserAccess(instance.TagState, ev);
                break;
            case ProjectorKind.UserDirectoryTag:
                instance.TagState = ProjectUserDirectory(instance.TagState, ev);
                break;
            case ProjectorKind.UserMonthlyReservationTag:
                instance.TagState = ProjectUserMonthlyReservation(instance.TagState, ev);
                break;
            case ProjectorKind.EquipmentTypeTag:
                instance.TagState = ProjectEquipmentType(instance.TagState, ev);
                break;
        }
    }

    #region Projector implementations (inline from EventSource)

    private static ITagStatePayload ProjectRoom(ITagStatePayload current, Event ev)
    {
        var state = current as RoomState ?? RoomState.Empty;
        return ev.Payload switch
        {
            RoomCreated created => RoomCreatedDecider.Create(created),
            RoomUpdated updated => RoomUpdatedDecider.Evolve(state, updated),
            RoomDeactivated deactivated => RoomDeactivatedDecider.Evolve(state, deactivated),
            RoomReactivated reactivated => RoomReactivatedDecider.Evolve(state, reactivated),
            _ => state
        };
    }

    private static ITagStatePayload ProjectReservation(ITagStatePayload current, Event ev)
    {
        var state = current as ReservationState ?? ReservationState.Empty;
        return ev.Payload switch
        {
            ReservationDraftCreated created => state.Evolve(created),
            ReservationHoldCommitted committed => state.Evolve(committed),
            ReservationConfirmed confirmed => state.Evolve(confirmed),
            ReservationCancelled cancelled => state.Evolve(cancelled),
            ReservationRejected rejected => state.Evolve(rejected),
            ReservationDetailsUpdated updated => state.Evolve(updated),
            ReservationExpiredCommitted expired => state.Evolve(expired),
            _ => state
        };
    }

    private static ITagStatePayload ProjectApprovalRequest(ITagStatePayload current, Event ev)
    {
        var state = current as ApprovalRequestState ?? ApprovalRequestState.Empty;
        return ev.Payload switch
        {
            ApprovalFlowStarted started => state.Evolve(started),
            ApprovalDecisionRecorded decision => state.Evolve(decision),
            _ => state
        };
    }

    private static ITagStatePayload ProjectRoomReservations(ITagStatePayload current, Event ev)
    {
        var state = current as RoomReservationsState ?? RoomReservationsState.Empty;
        return ev.Payload switch
        {
            ReservationHoldCommitted committed => state.AddOrUpdateReservation(
                committed.ReservationId,
                committed.StartTime,
                committed.EndTime,
                committed.Purpose,
                committed.OrganizerId,
                ReservationSlotStatus.Held),
            ReservationConfirmed confirmed => state.AddOrUpdateReservation(
                confirmed.ReservationId,
                confirmed.StartTime,
                confirmed.EndTime,
                confirmed.Purpose,
                confirmed.OrganizerId,
                ReservationSlotStatus.Confirmed),
            ReservationCancelled cancelled => state.RemoveReservation(cancelled.ReservationId),
            ReservationRejected rejected => state.RemoveReservation(rejected.ReservationId),
            ReservationExpiredCommitted expired => state.RemoveReservation(expired.ReservationId),
            _ => state
        };
    }

    private static ITagStatePayload ProjectRoomDailyActivity(ITagStatePayload current, Event ev)
    {
        var state = current as RoomDailyActivityState ?? RoomDailyActivityState.Empty;
        return ev.Payload switch
        {
            ReservationConfirmed confirmed => state.AddReservation(
                confirmed.ReservationId,
                confirmed.StartTime,
                confirmed.EndTime,
                confirmed.Purpose,
                confirmed.OrganizerId),
            ReservationCancelled cancelled => state.RemoveReservation(cancelled.ReservationId),
            ReservationRejected rejected => state.RemoveReservation(rejected.ReservationId),
            ReservationExpiredCommitted expired => state.RemoveReservation(expired.ReservationId),
            _ => state
        };
    }

    private static ITagStatePayload ProjectUserAccess(ITagStatePayload current, Event ev)
    {
        var state = current as UserAccessState ?? UserAccessState.Empty;
        return ev.Payload switch
        {
            UserAccessGranted granted => state.Evolve(granted),
            UserRoleGranted roleGranted => state.Evolve(roleGranted),
            UserRoleRevoked roleRevoked => state.Evolve(roleRevoked),
            UserAccessDeactivated deactivated => state.Evolve(deactivated),
            UserAccessReactivated reactivated => state.Evolve(reactivated),
            _ => state
        };
    }

    private static ITagStatePayload ProjectUserDirectory(ITagStatePayload current, Event ev)
    {
        var state = current as UserDirectoryState ?? UserDirectoryState.Empty;
        return ev.Payload switch
        {
            UserRegistered registered => state.Evolve(registered),
            UserProfileUpdated updated => state.Evolve(updated),
            UserDeactivated deactivated => state.Evolve(deactivated),
            UserReactivated reactivated => state.Evolve(reactivated),
            ExternalIdentityLinked linked => state.Evolve(linked),
            ExternalIdentityUnlinked unlinked => state.Evolve(unlinked),
            _ => state
        };
    }

    private static ITagStatePayload ProjectUserMonthlyReservation(ITagStatePayload current, Event ev)
    {
        var state = current as UserMonthlyReservationState ?? UserMonthlyReservationState.Empty;
        return ev.Payload switch
        {
            ReservationDraftCreated created => state.RegisterRequest(
                created.OrganizerId,
                new DateOnly(created.StartTime.Year, created.StartTime.Month, 1)),
            ReservationRejected => state.RegisterRejection(),
            _ => state
        };
    }

    private static ITagStatePayload ProjectEquipmentType(ITagStatePayload current, Event ev)
    {
        var state = current as EquipmentTypeState ?? EquipmentTypeState.Empty;
        return ev.Payload switch
        {
            EquipmentTypeCreated created => state.Evolve(created),
            EquipmentTypeUpdated updated => state.Evolve(updated),
            _ => state
        };
    }

    #endregion

    #region Helper methods

    private static ITagStatePayload GetInitialState(ProjectorKind kind) => kind switch
    {
        ProjectorKind.RoomTag => RoomState.Empty,
        ProjectorKind.ReservationTag => ReservationState.Empty,
        ProjectorKind.ApprovalRequestTag => ApprovalRequestState.Empty,
        ProjectorKind.RoomReservationsTag => RoomReservationsState.Empty,
        ProjectorKind.RoomDailyActivityTag => RoomDailyActivityState.Empty,
        ProjectorKind.UserAccessTag => UserAccessState.Empty,
        ProjectorKind.UserDirectoryTag => UserDirectoryState.Empty,
        ProjectorKind.UserMonthlyReservationTag => UserMonthlyReservationState.Empty,
        ProjectorKind.EquipmentTypeTag => EquipmentTypeState.Empty,
        _ => new EmptyTagStatePayload()
    };

    private static string SerializeTagState<T>(ITagStatePayload tagState,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : ITagStatePayload
    {
        if (tagState is EmptyTagStatePayload)
            return "{}";
        return JsonSerializer.Serialize((T)tagState, typeInfo);
    }

    private static ITagStatePayload DeserializeTagState<T>(string? json,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : ITagStatePayload
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new EmptyTagStatePayload();
        try
        {
            var deserialized = JsonSerializer.Deserialize(json, typeInfo);
            return deserialized is null ? new EmptyTagStatePayload() : (ITagStatePayload)deserialized;
        }
        catch
        {
            return new EmptyTagStatePayload();
        }
    }

    private static bool TryGetStringProperty(JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return true;
        }
        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        if (obj.TryGetProperty(pascal, out prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    private static string ExecuteCountQuery(string queryParamsJson, WeatherForecastMultiProjection state)
    {
        var query = ParseLocationQuery(queryParamsJson);
        var active = state.Forecasts.Values.AsEnumerable();
        if (!query.IncludeDeleted)
            active = active.Where(f => !f.IsDeleted);
        if (!string.IsNullOrWhiteSpace(query.ForecastId))
            active = active.Where(f => f.ForecastId == query.ForecastId);
        var count = string.IsNullOrEmpty(query.LocationFilter)
            ? active.Count()
            : active.Count(f => f.Location.Contains(query.LocationFilter, StringComparison.OrdinalIgnoreCase));
        return JsonSerializer.Serialize(new CountResult(count), WasmJsonContext.Default.CountResult);
    }

    private static string ExecuteListQueryInternal(string queryParamsJson, WeatherForecastMultiProjection state)
    {
        var query = ParseLocationQuery(queryParamsJson);
        var items = state.Forecasts.Values.AsEnumerable();
        if (!query.IncludeDeleted)
            items = items.Where(f => !f.IsDeleted);
        if (!string.IsNullOrWhiteSpace(query.ForecastId))
            items = items.Where(f => f.ForecastId == query.ForecastId);
        if (!string.IsNullOrEmpty(query.LocationFilter))
            items = items.Where(f => f.Location.Contains(query.LocationFilter, StringComparison.OrdinalIgnoreCase));

        return JsonSerializer.Serialize(
            items.OrderByDescending(f => f.CreatedAt).ToList(),
            DomainJsonContext.Default.ListWeatherForecastItem);
    }

    private static LocationQuery ParseLocationQuery(string json) =>
        string.IsNullOrWhiteSpace(json) || json == "{}"
            ? new LocationQuery(null)
            : JsonSerializer.Deserialize(json, WasmJsonContext.Default.LocationQuery) ?? new LocationQuery(null);

    private static List<ITag> ExtractTags(IEventPayload payload) => payload switch
    {
        // Weather
        WeatherForecastCreated created => [new WeatherForecastTag(created.ForecastId)],
        WeatherForecastLocationUpdated updated => [new WeatherForecastTag(updated.ForecastId)],
        WeatherForecastDeleted deleted => [new WeatherForecastTag(deleted.ForecastId)],
        // Room
        RoomCreated created => [new RoomTag(created.RoomId)],
        RoomUpdated updated => [new RoomTag(updated.RoomId)],
        RoomDeactivated deactivated => [new RoomTag(deactivated.RoomId)],
        RoomReactivated reactivated => [new RoomTag(reactivated.RoomId)],
        // Reservation
        ReservationDraftCreated created => [new ReservationTag(created.ReservationId), new RoomTag(created.RoomId)],
        ReservationHoldCommitted committed => [new ReservationTag(committed.ReservationId), new RoomTag(committed.RoomId)],
        ReservationConfirmed confirmed => [new ReservationTag(confirmed.ReservationId), new RoomTag(confirmed.RoomId)],
        ReservationCancelled cancelled => [new ReservationTag(cancelled.ReservationId), new RoomTag(cancelled.RoomId)],
        ReservationRejected rejected => [new ReservationTag(rejected.ReservationId), new RoomTag(rejected.RoomId)],
        ReservationDetailsUpdated updated => [new ReservationTag(updated.ReservationId), new RoomTag(updated.RoomId)],
        ReservationExpiredCommitted expired => [new ReservationTag(expired.ReservationId), new RoomTag(expired.RoomId)],
        // ApprovalRequest
        ApprovalFlowStarted started => [new ApprovalRequestTag(started.ApprovalRequestId), new ReservationTag(started.ReservationId)],
        ApprovalDecisionRecorded decision => [new ApprovalRequestTag(decision.ApprovalRequestId), new ReservationTag(decision.ReservationId)],
        // UserAccess
        UserAccessGranted granted => [new UserAccessTag(granted.UserId)],
        UserRoleGranted roleGranted => [new UserAccessTag(roleGranted.UserId)],
        UserRoleRevoked roleRevoked => [new UserAccessTag(roleRevoked.UserId)],
        UserAccessDeactivated deactivated => [new UserAccessTag(deactivated.UserId)],
        UserAccessReactivated reactivated => [new UserAccessTag(reactivated.UserId)],
        // UserDirectory
        UserRegistered registered => [new UserTag(registered.UserId)],
        UserProfileUpdated updated => [new UserTag(updated.UserId)],
        UserDeactivated deactivated => [new UserTag(deactivated.UserId)],
        UserReactivated reactivated => [new UserTag(reactivated.UserId)],
        ExternalIdentityLinked linked => [new UserTag(linked.UserId)],
        ExternalIdentityUnlinked unlinked => [new UserTag(unlinked.UserId)],
        // EquipmentType
        EquipmentTypeCreated created => [new EquipmentTypeTag(created.EquipmentTypeId)],
        EquipmentTypeUpdated updated => [new EquipmentTypeTag(updated.EquipmentTypeId)],
        _ => []
    };

    private static Event CreateEvent(IEventPayload payload, string eventType)
    {
        var id = Guid.NewGuid();
        var sortableId = SortableUniqueId.Generate(DateTime.UtcNow, id);
        var metadata = new EventMetadata(id.ToString(), eventType, "wasm");
        return new Event(payload, sortableId, eventType, id, metadata, []);
    }

    private static WeatherForecastMultiProjection DeserializeWeatherMultiState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return WeatherForecastMultiProjection.GenerateInitialPayload();

        var state = JsonSerializer.Deserialize(json, DomainJsonContext.Default.WeatherForecastMultiProjection);
        if (state == null)
            return WeatherForecastMultiProjection.GenerateInitialPayload();

        return state.Forecasts == null
            ? state with { Forecasts = new Dictionary<string, WeatherForecastItem>() }
            : state;
    }

    private static ProjectorKind ResolveProjectorKind(string projectorType)
    {
        var normalized = projectorType.Trim();

        return normalized switch
        {
            "WeatherForecastProjector" => ProjectorKind.WeatherTag,
            "WeatherForecastProjectorWithTagStateProjector" => ProjectorKind.WeatherTag,
            "WeatherForecastProjection" => ProjectorKind.WeatherList,
            "RoomProjector" => ProjectorKind.RoomTag,
            "ReservationProjector" => ProjectorKind.ReservationTag,
            "ApprovalRequestProjector" => ProjectorKind.ApprovalRequestTag,
            "RoomReservationsProjector" => ProjectorKind.RoomReservationsTag,
            "RoomDailyActivityProjector" => ProjectorKind.RoomDailyActivityTag,
            "UserAccessProjector" => ProjectorKind.UserAccessTag,
            "UserDirectoryProjector" => ProjectorKind.UserDirectoryTag,
            "UserMonthlyReservationProjector" => ProjectorKind.UserMonthlyReservationTag,
            "EquipmentTypeProjector" => ProjectorKind.EquipmentTypeTag,
            _ => ResolveProjectorKindFuzzy(normalized.ToLowerInvariant())
        };
    }

    private static ProjectorKind ResolveProjectorKindFuzzy(string normalized)
    {
        if (normalized.Contains("weatherforecastprojector"))
            return ProjectorKind.WeatherTag;
        if (normalized.Contains("weatherforecast") || normalized.Contains("weather"))
            return ProjectorKind.WeatherList;
        return ProjectorKind.Unknown;
    }

    private static ProjectorInstanceState? GetInstance(int instanceId)
    {
        lock (_gate)
        {
            return _instances.TryGetValue(instanceId, out var instance) ? instance : null;
        }
    }

    private static unsafe string ReadString(int ptr, int len)
    {
        if (ptr == 0 || len <= 0) return "";
        return System.Text.Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)ptr, len));
    }

    private static unsafe long WriteString(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length == 0) return 0;
        var ptr = NativeMemory.Alloc((nuint)bytes.Length);
        var span = new Span<byte>((void*)ptr, bytes.Length);
        bytes.AsSpan().CopyTo(span);
        return Pack((nint)ptr, bytes.Length);
    }

    private static long Pack(nint ptr, int len)
    {
        return ((long)(uint)ptr << 32) | (uint)len;
    }

    #endregion
}
