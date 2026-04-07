using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Dcb.EventSource.ClassRoom;
using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.EventSource.MeetingRoom.Equipment;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.User;
using Dcb.EventSource.Projections;
using Dcb.EventSource.Queries;
using Dcb.EventSource.Student;
using Dcb.EventSource.Weather;
using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.Events.Student;
using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.States.Weather;
using Dcb.ImmutableModels.Tags;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.EquipmentItem;
using Dcb.MeetingRoomModels.Events.EquipmentReservation;
using Dcb.MeetingRoomModels.Events.EquipmentType;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.Events.UserAccess;
using Dcb.MeetingRoomModels.Events.UserDirectory;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.EquipmentItem;
using Dcb.MeetingRoomModels.States.EquipmentReservation;
using Dcb.MeetingRoomModels.States.EquipmentType;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.States.UserMonthlyReservation;
using Dcb.MeetingRoomModels.Tags;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;

namespace SekibanDcbDecider.Wasm;

internal static class WasmDispatcher
{
    private sealed record ProjectorRegistration(
        bool IsMultiProjector,
        Func<object> CreateInitialState,
        Func<object, Event, List<ITag>, object> ApplyEvent,
        Func<object, string> SerializeState,
        Func<string, object> DeserializeState);

    private sealed record QueryRegistration(
        Func<object, ProjectionInstanceState, string, string> Execute);

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class WasmQueryContext(
        int? safeVersion,
        string? safeWindowThreshold,
        int? unsafeVersion) : IQueryContext
    {
        private static readonly IServiceProvider Services = new NullServiceProvider();

        public IServiceProvider ServiceProvider => Services;
        public int? SafeVersion => safeVersion;
        public string? SafeWindowThreshold => safeWindowThreshold;
        public DateTime? SafeWindowThresholdTime =>
            string.IsNullOrWhiteSpace(safeWindowThreshold)
                ? null
                : new SortableUniqueId(safeWindowThreshold).GetDateTime();
        public int? UnsafeVersion => unsafeVersion;
        public T GetService<T>() where T : notnull => throw new InvalidOperationException("WASM query context does not expose services.");
        public T? GetServiceOrDefault<T>() where T : class => null;
    }

    private static readonly DcbDomainTypes ProjectionDomainTypes = BuildProjectionDomainTypes();
    private static readonly Dictionary<string, Func<string, IEventPayload?>> EventPayloadReaders = CreateEventPayloadReaders();
    private static readonly Dictionary<string, Func<string, ITag>> TagReaders = CreateTagReaders();
    private static readonly Dictionary<string, ProjectorRegistration> Projectors = CreateProjectors();
    private static readonly Dictionary<string, QueryRegistration> Queries = CreateQueries();

    public sealed class ProjectionInstanceState
    {
        public required string ProjectorName { get; init; }
        public required bool IsMultiProjector { get; init; }
        public required object State { get; set; }
        public int Version { get; set; }
        public string? LastSortableUniqueId { get; set; }
    }

    public static ProjectionInstanceState? CreateInstance(string projectorName)
    {
        if (!Projectors.TryGetValue(projectorName, out ProjectorRegistration? registration))
        {
            return null;
        }

        return new ProjectionInstanceState
        {
            ProjectorName = projectorName,
            IsMultiProjector = registration.IsMultiProjector,
            State = registration.CreateInitialState(),
            Version = 0
        };
    }

    public static void ApplyEvent(
        ProjectionInstanceState instance,
        string eventType,
        string payloadJson,
        string metadataJson)
    {
        if (!Projectors.TryGetValue(instance.ProjectorName, out ProjectorRegistration? registration))
        {
            return;
        }

        if (!EventPayloadReaders.TryGetValue(eventType, out Func<string, IEventPayload?>? payloadReader))
        {
            return;
        }

        IEventPayload? payload = payloadReader(payloadJson);
        if (payload is null)
        {
            return;
        }

        WasmEventMetadata metadata = ParseMetadata(metadataJson);
        string sortableUniqueId = string.IsNullOrWhiteSpace(metadata.SortableUniqueId)
            ? SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid())
            : metadata.SortableUniqueId!;
        List<string> tagStrings = metadata.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList() ?? [];
        List<ITag> tags = ParseTags(tagStrings);
        Event ev = new(
            payload,
            sortableUniqueId,
            eventType,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString("N"), eventType, "wasm"),
            tagStrings);

        instance.State = registration.ApplyEvent(instance.State, ev, tags);
        instance.Version++;
        instance.LastSortableUniqueId = sortableUniqueId;
    }

    public static string ExecuteQuery(
        ProjectionInstanceState instance,
        string queryType,
        string queryParamsJson)
    {
        if (!Queries.TryGetValue(queryType, out QueryRegistration? query))
        {
            return "null";
        }

        return query.Execute(instance.State, instance, queryParamsJson);
    }

    public static string ExecuteListQuery(
        ProjectionInstanceState instance,
        string queryType,
        string queryParamsJson)
    {
        if (!Queries.TryGetValue(queryType, out QueryRegistration? query))
        {
            return "[]";
        }

        return query.Execute(instance.State, instance, queryParamsJson);
    }

    public static string SerializeState(ProjectionInstanceState instance)
    {
        if (!Projectors.TryGetValue(instance.ProjectorName, out ProjectorRegistration? registration))
        {
            return "{}";
        }

        return registration.SerializeState(instance.State);
    }

    public static void RestoreState(ProjectionInstanceState instance, string stateJson)
    {
        if (!Projectors.TryGetValue(instance.ProjectorName, out ProjectorRegistration? registration))
        {
            return;
        }

        instance.State = registration.DeserializeState(stateJson);
    }

    private static Dictionary<string, ProjectorRegistration> CreateProjectors() =>
        new(StringComparer.Ordinal)
        {
            [StudentProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => new EmptyTagStatePayload(),
                applyEvent: StudentProjector.Project,
                serializeState: state =>
                {
                    if (state is EmptyTagStatePayload)
                    {
                        return "{}";
                    }

                    return JsonSerializer.Serialize((StudentState)state, WasmJsonContext.Default.StudentState);
                },
                deserializeState: json =>
                    DeserializeTagState(
                        json,
                        () => new EmptyTagStatePayload(),
                        WasmJsonContext.Default.StudentState)),
            [ClassRoomProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => new EmptyTagStatePayload(),
                applyEvent: ClassRoomProjector.Project,
                serializeState: SerializeClassRoomState,
                deserializeState: DeserializeClassRoomState),
            [WeatherForecastProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => WeatherForecastState.Empty,
                applyEvent: WeatherForecastProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (WeatherForecastState)state,
                    WasmJsonContext.Default.WeatherForecastState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => WeatherForecastState.Empty,
                    WasmJsonContext.Default.WeatherForecastState)),
            [ApprovalRequestProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => ApprovalRequestState.Empty,
                applyEvent: ApprovalRequestProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (ApprovalRequestState)state,
                    WasmJsonContext.Default.ApprovalRequestState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => ApprovalRequestState.Empty,
                    WasmJsonContext.Default.ApprovalRequestState)),
            [EquipmentTypeProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => EquipmentTypeState.Empty,
                applyEvent: EquipmentTypeProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (EquipmentTypeState)state,
                    WasmJsonContext.Default.EquipmentTypeState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => EquipmentTypeState.Empty,
                    WasmJsonContext.Default.EquipmentTypeState)),
            [ReservationProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => ReservationState.Empty,
                applyEvent: ReservationProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (ReservationState)state,
                    WasmJsonContext.Default.ReservationState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => ReservationState.Empty,
                    WasmJsonContext.Default.ReservationState)),
            [RoomProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => RoomState.Empty,
                applyEvent: RoomProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (RoomState)state,
                    WasmJsonContext.Default.RoomState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => RoomState.Empty,
                    WasmJsonContext.Default.RoomState)),
            [RoomDailyActivityProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => RoomDailyActivityState.Empty,
                applyEvent: RoomDailyActivityProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (RoomDailyActivityState)state,
                    WasmJsonContext.Default.RoomDailyActivityState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => RoomDailyActivityState.Empty,
                    WasmJsonContext.Default.RoomDailyActivityState)),
            [RoomReservationsProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => RoomReservationsState.Empty,
                applyEvent: RoomReservationsProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (RoomReservationsState)state,
                    WasmJsonContext.Default.RoomReservationsState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => RoomReservationsState.Empty,
                    WasmJsonContext.Default.RoomReservationsState)),
            [UserAccessProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => UserAccessState.Empty,
                applyEvent: UserAccessProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (UserAccessState)state,
                    WasmJsonContext.Default.UserAccessState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => UserAccessState.Empty,
                    WasmJsonContext.Default.UserAccessState)),
            [UserDirectoryProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => UserDirectoryState.Empty,
                applyEvent: UserDirectoryProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (UserDirectoryState)state,
                    WasmJsonContext.Default.UserDirectoryState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => UserDirectoryState.Empty,
                    WasmJsonContext.Default.UserDirectoryState)),
            [UserMonthlyReservationProjector.ProjectorName] = CreateTagProjector(
                isMultiProjector: false,
                initialState: () => UserMonthlyReservationState.Empty,
                applyEvent: UserMonthlyReservationProjector.Project,
                serializeState: state => JsonSerializer.Serialize(
                    (UserMonthlyReservationState)state,
                    WasmJsonContext.Default.UserMonthlyReservationState),
                deserializeState: json => DeserializeTagState(
                    json,
                    () => UserMonthlyReservationState.Empty,
                    WasmJsonContext.Default.UserMonthlyReservationState)),
            [StudentListProjection.MultiProjectorName] = CreateMultiProjector(
                StudentListProjection.GenerateInitialPayload,
                (state, ev, tags) => StudentListProjection.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.StudentListProjection),
            [ClassRoomListProjection.MultiProjectorName] = CreateMultiProjector(
                ClassRoomListProjection.GenerateInitialPayload,
                (state, ev, tags) => ClassRoomListProjection.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.ClassRoomListProjection),
            [WeatherForecastProjection.MultiProjectorName] = CreateMultiProjector(
                WeatherForecastProjection.GenerateInitialPayload,
                (state, ev, tags) => WeatherForecastProjection.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.WeatherForecastProjection),
            [WeatherForecastProjectorWithTagStateProjector.MultiProjectorName] = CreateMultiProjector(
                WeatherForecastProjectorWithTagStateProjector.GenerateInitialPayload,
                (state, ev, tags) => WeatherForecastProjectorWithTagStateProjector.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                SerializeWeatherTagProjectionState,
                DeserializeWeatherTagProjectionState),
            [ApprovalRequestListProjection.MultiProjectorName] = CreateMultiProjector(
                ApprovalRequestListProjection.GenerateInitialPayload,
                (state, ev, tags) => ApprovalRequestListProjection.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.ApprovalRequestListProjection),
            [EquipmentTypeListProjection.MultiProjectorName] = CreateMultiProjector(
                EquipmentTypeListProjection.GenerateInitialPayload,
                (state, ev, tags) => EquipmentTypeListProjection.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.EquipmentTypeListProjection),
            [ReservationListProjection.MultiProjectorName] = CreateMultiProjector(
                ReservationListProjection.GenerateInitialPayload,
                (state, ev, tags) => ReservationListProjection.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.ReservationListProjection),
            [RoomListProjection.MultiProjectorName] = CreateMultiProjector(
                RoomListProjection.GenerateInitialPayload,
                (state, ev, tags) => RoomListProjection.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.RoomListProjection),
            [StudentSummaries.MultiProjectorName] = CreateMultiProjector(
                StudentSummaries.GenerateInitialPayload,
                (state, ev, tags) => StudentSummaries.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.StudentSummaries),
            [UserAccessListProjection.MultiProjectorName] = CreateMultiProjector(
                UserAccessListProjection.GenerateInitialPayload,
                (state, ev, tags) => UserAccessListProjection.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.UserAccessListProjection),
            [UserDirectoryListProjection.MultiProjectorName] = CreateMultiProjector(
                UserDirectoryListProjection.GenerateInitialPayload,
                (state, ev, tags) => UserDirectoryListProjection.Project(
                    state,
                    ev,
                    tags,
                    ProjectionDomainTypes,
                    new SortableUniqueId(ev.SortableUniqueIdValue)),
                WasmJsonContext.Default.UserDirectoryListProjection)
        };

    private static Dictionary<string, QueryRegistration> CreateQueries() =>
        new(StringComparer.Ordinal)
        {
            [nameof(GetStudentListQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetStudentListQuery,
                WasmJsonContext.Default.ListStudentState,
                (StudentListProjection projector, GetStudentListQuery query, IQueryContext context) =>
                    GetStudentListQuery.HandleFilter(projector, query, context),
                (IEnumerable<StudentState> items, GetStudentListQuery query, IQueryContext context) =>
                    GetStudentListQuery.HandleSort(items, query, context)),
            [nameof(GetClassRoomListQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetClassRoomListQuery,
                WasmJsonContext.Default.ListClassRoomItem,
                (ClassRoomListProjection projector, GetClassRoomListQuery query, IQueryContext context) =>
                    GetClassRoomListQuery.HandleFilter(projector, query, context),
                (IEnumerable<ClassRoomItem> items, GetClassRoomListQuery query, IQueryContext context) =>
                    GetClassRoomListQuery.HandleSort(items, query, context)),
            [nameof(GetWeatherForecastListQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetWeatherForecastListQuery,
                WasmJsonContext.Default.ListWeatherForecastItem,
                (WeatherForecastProjection projector, GetWeatherForecastListQuery query, IQueryContext context) =>
                    GetWeatherForecastListQuery.HandleFilter(projector, query, context),
                (IEnumerable<WeatherForecastItem> items, GetWeatherForecastListQuery query, IQueryContext context) =>
                    GetWeatherForecastListQuery.HandleSort(items, query, context)),
            [nameof(GetWeatherForecastListGenericQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetWeatherForecastListGenericQuery,
                WasmJsonContext.Default.ListWeatherForecastItem,
                (WeatherForecastProjection projector, GetWeatherForecastListGenericQuery query, IQueryContext context) =>
                    GetWeatherForecastListGenericQuery.HandleFilter(projector, query, context),
                (IEnumerable<WeatherForecastItem> items, GetWeatherForecastListGenericQuery query, IQueryContext context) =>
                    GetWeatherForecastListGenericQuery.HandleSort(items, query, context)),
            [nameof(GetWeatherForecastListSingleQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetWeatherForecastListSingleQuery,
                WasmJsonContext.Default.ListWeatherForecastItem,
                (WeatherForecastProjectorWithTagStateProjector projector, GetWeatherForecastListSingleQuery query, IQueryContext context) =>
                    GetWeatherForecastListSingleQuery.HandleFilter(projector, query, context),
                (IEnumerable<WeatherForecastItem> items, GetWeatherForecastListSingleQuery query, IQueryContext context) =>
                    GetWeatherForecastListSingleQuery.HandleSort(items, query, context)),
            [nameof(GetReservationListQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetReservationListQuery,
                WasmJsonContext.Default.ListReservationListItem,
                (ReservationListProjection projector, GetReservationListQuery query, IQueryContext context) =>
                    GetReservationListQuery.HandleFilter(projector, query, context),
                (IEnumerable<ReservationListItem> items, GetReservationListQuery query, IQueryContext context) =>
                    GetReservationListQuery.HandleSort(items, query, context)),
            [nameof(GetRoomListQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetRoomListQuery,
                WasmJsonContext.Default.ListRoomListItem,
                (RoomListProjection projector, GetRoomListQuery query, IQueryContext context) =>
                    GetRoomListQuery.HandleFilter(projector, query, context),
                (IEnumerable<RoomListItem> items, GetRoomListQuery query, IQueryContext context) =>
                    GetRoomListQuery.HandleSort(items, query, context)),
            [nameof(GetUserAccessListQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetUserAccessListQuery,
                WasmJsonContext.Default.ListUserAccessListItem,
                (UserAccessListProjection projector, GetUserAccessListQuery query, IQueryContext context) =>
                    GetUserAccessListQuery.HandleFilter(projector, query, context),
                (IEnumerable<UserAccessListItem> items, GetUserAccessListQuery query, IQueryContext context) =>
                    GetUserAccessListQuery.HandleSort(items, query, context)),
            [nameof(GetUserDirectoryListQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetUserDirectoryListQuery,
                WasmJsonContext.Default.ListUserDirectoryListItem,
                (UserDirectoryListProjection projector, GetUserDirectoryListQuery query, IQueryContext context) =>
                    GetUserDirectoryListQuery.HandleFilter(projector, query, context),
                (IEnumerable<UserDirectoryListItem> items, GetUserDirectoryListQuery query, IQueryContext context) =>
                    GetUserDirectoryListQuery.HandleSort(items, query, context)),
            [nameof(GetEquipmentTypeListQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetEquipmentTypeListQuery,
                WasmJsonContext.Default.ListEquipmentTypeListItem,
                (EquipmentTypeListProjection projector, GetEquipmentTypeListQuery query, IQueryContext context) =>
                    GetEquipmentTypeListQuery.HandleFilter(projector, query, context),
                (IEnumerable<EquipmentTypeListItem> items, GetEquipmentTypeListQuery query, IQueryContext context) =>
                    GetEquipmentTypeListQuery.HandleSort(items, query, context)),
            [nameof(GetApprovalInboxQuery)] = CreateListQuery(
                WasmJsonContext.Default.GetApprovalInboxQuery,
                WasmJsonContext.Default.ListApprovalInboxItem,
                (ApprovalRequestListProjection projector, GetApprovalInboxQuery query, IQueryContext context) =>
                    GetApprovalInboxQuery.HandleFilter(projector, query, context),
                (IEnumerable<ApprovalInboxItem> items, GetApprovalInboxQuery query, IQueryContext context) =>
                    GetApprovalInboxQuery.HandleSort(items, query, context)),
            [nameof(GetWeatherForecastCountQuery)] = CreateSingleQuery(
                WasmJsonContext.Default.GetWeatherForecastCountQuery,
                WasmJsonContext.Default.WeatherForecastCountResult,
                (WeatherForecastProjection projector, GetWeatherForecastCountQuery query, IQueryContext context) =>
                    GetWeatherForecastCountQuery.HandleQuery(projector, query, context)),
            [nameof(GetWeatherForecastCountGenericQuery)] = CreateSingleQuery(
                WasmJsonContext.Default.GetWeatherForecastCountGenericQuery,
                WasmJsonContext.Default.WeatherForecastCountResult,
                (WeatherForecastProjection projector, GetWeatherForecastCountGenericQuery query, IQueryContext context) =>
                    GetWeatherForecastCountGenericQuery.HandleQuery(projector, query, context)),
            [nameof(GetWeatherForecastCountSingleQuery)] = CreateSingleQuery(
                WasmJsonContext.Default.GetWeatherForecastCountSingleQuery,
                WasmJsonContext.Default.WeatherForecastCountResult,
                (WeatherForecastProjectorWithTagStateProjector projector, GetWeatherForecastCountSingleQuery query, IQueryContext context) =>
                    GetWeatherForecastCountSingleQuery.HandleQuery(projector, query, context))
        };

    private static Dictionary<string, Func<string, IEventPayload?>> CreateEventPayloadReaders()
    {
        var readers = new Dictionary<string, Func<string, IEventPayload?>>(StringComparer.Ordinal);

        RegisterEvent(readers, WasmJsonContext.Default.ClassRoomCreated);
        RegisterEvent(readers, WasmJsonContext.Default.StudentDroppedFromClassRoom);
        RegisterEvent(readers, WasmJsonContext.Default.StudentEnrolledInClassRoom);
        RegisterEvent(readers, WasmJsonContext.Default.StudentCreated);
        RegisterEvent(readers, WasmJsonContext.Default.LocationNameChanged);
        RegisterEvent(readers, WasmJsonContext.Default.WeatherForecastCreated);
        RegisterEvent(readers, WasmJsonContext.Default.WeatherForecastDeleted);
        RegisterEvent(readers, WasmJsonContext.Default.WeatherForecastUpdated);
        RegisterEvent(readers, WasmJsonContext.Default.ApprovalDecisionRecorded);
        RegisterEvent(readers, WasmJsonContext.Default.ApprovalFlowStarted);
        RegisterEvent(readers, WasmJsonContext.Default.EquipmentItemRegistered);
        RegisterEvent(readers, WasmJsonContext.Default.EquipmentItemRetired);
        RegisterEvent(readers, WasmJsonContext.Default.EquipmentCheckedOut);
        RegisterEvent(readers, WasmJsonContext.Default.EquipmentItemAssigned);
        RegisterEvent(readers, WasmJsonContext.Default.EquipmentReservationCancelled);
        RegisterEvent(readers, WasmJsonContext.Default.EquipmentReservationCreated);
        RegisterEvent(readers, WasmJsonContext.Default.EquipmentReturned);
        RegisterEvent(readers, WasmJsonContext.Default.EquipmentTypeCreated);
        RegisterEvent(readers, WasmJsonContext.Default.EquipmentTypeUpdated);
        RegisterEvent(readers, WasmJsonContext.Default.ReservationCancelledEvent);
        RegisterEvent(readers, WasmJsonContext.Default.ReservationConfirmedEvent);
        RegisterEvent(readers, WasmJsonContext.Default.ReservationDetailsUpdated);
        RegisterEvent(readers, WasmJsonContext.Default.ReservationDraftCreated);
        RegisterEvent(readers, WasmJsonContext.Default.ReservationExpiredCommitted);
        RegisterEvent(readers, WasmJsonContext.Default.ReservationHoldCommitted);
        RegisterEvent(readers, WasmJsonContext.Default.ReservationRejectedEvent);
        RegisterEvent(readers, WasmJsonContext.Default.RoomCreated);
        RegisterEvent(readers, WasmJsonContext.Default.RoomDeactivated);
        RegisterEvent(readers, WasmJsonContext.Default.RoomReactivated);
        RegisterEvent(readers, WasmJsonContext.Default.RoomUpdated);
        RegisterEvent(readers, WasmJsonContext.Default.UserAccessDeactivatedEvent);
        RegisterEvent(readers, WasmJsonContext.Default.UserAccessGranted);
        RegisterEvent(readers, WasmJsonContext.Default.UserAccessReactivated);
        RegisterEvent(readers, WasmJsonContext.Default.UserRoleGranted);
        RegisterEvent(readers, WasmJsonContext.Default.UserRoleRevoked);
        RegisterEvent(readers, WasmJsonContext.Default.ExternalIdentityLinked);
        RegisterEvent(readers, WasmJsonContext.Default.ExternalIdentityUnlinked);
        RegisterEvent(readers, WasmJsonContext.Default.UserDeactivated);
        RegisterEvent(readers, WasmJsonContext.Default.UserProfileUpdated);
        RegisterEvent(readers, WasmJsonContext.Default.UserReactivated);
        RegisterEvent(readers, WasmJsonContext.Default.UserRegistered);

        return readers;
    }

    private static Dictionary<string, Func<string, ITag>> CreateTagReaders()
    {
        var readers = new Dictionary<string, Func<string, ITag>>(StringComparer.Ordinal);
        RegisterTagGroup<ClassRoomTag>(readers);
        RegisterTagGroup<StudentCodeTag>(readers);
        RegisterTagGroup<StudentTag>(readers);
        RegisterTagGroup<WeatherForecastTag>(readers);
        RegisterTagGroup<YearlyStudentsTag>(readers);
        RegisterTagGroup<ApprovalRequestTag>(readers);
        RegisterTagGroup<EquipmentItemTag>(readers);
        RegisterTagGroup<EquipmentReservationTag>(readers);
        RegisterTagGroup<EquipmentTypeTag>(readers);
        RegisterTagGroup<ReservationTag>(readers);
        RegisterTagGroup<RoomDailyActivityTag>(readers);
        RegisterTagGroup<RoomTag>(readers);
        RegisterTagGroup<UserAccessTag>(readers);
        RegisterTagGroup<UserMonthlyReservationTag>(readers);
        RegisterTagGroup<UserTag>(readers);
        return readers;
    }

    private static DcbDomainTypes BuildProjectionDomainTypes()
    {
        var builder = new AotDomainTypesBuilder(WasmJsonContext.Default.Options);
        builder.TagTypes.RegisterTagGroupType<ClassRoomTag>();
        builder.TagTypes.RegisterTagGroupType<StudentCodeTag>();
        builder.TagTypes.RegisterTagGroupType<StudentTag>();
        builder.TagTypes.RegisterTagGroupType<WeatherForecastTag>();
        builder.TagTypes.RegisterTagGroupType<YearlyStudentsTag>();
        builder.TagTypes.RegisterTagGroupType<ApprovalRequestTag>();
        builder.TagTypes.RegisterTagGroupType<EquipmentItemTag>();
        builder.TagTypes.RegisterTagGroupType<EquipmentReservationTag>();
        builder.TagTypes.RegisterTagGroupType<EquipmentTypeTag>();
        builder.TagTypes.RegisterTagGroupType<ReservationTag>();
        builder.TagTypes.RegisterTagGroupType<RoomDailyActivityTag>();
        builder.TagTypes.RegisterTagGroupType<RoomTag>();
        builder.TagTypes.RegisterTagGroupType<UserAccessTag>();
        builder.TagTypes.RegisterTagGroupType<UserMonthlyReservationTag>();
        builder.TagTypes.RegisterTagGroupType<UserTag>();
        builder.TagStatePayloadTypes.Register(nameof(WeatherForecastState), WasmJsonContext.Default.WeatherForecastState);
        return builder.Build();
    }

    private static void RegisterEvent<T>(
        IDictionary<string, Func<string, IEventPayload?>> readers,
        JsonTypeInfo<T> typeInfo)
        where T : IEventPayload =>
        readers[typeInfo.Type.Name] = json => JsonSerializer.Deserialize(json, typeInfo);

    private static void RegisterTagGroup<TTagGroup>(
        IDictionary<string, Func<string, ITag>> readers)
        where TTagGroup : ITagGroup<TTagGroup> =>
        readers[TTagGroup.TagGroupName] = content => TTagGroup.FromContent(content);

    private static ProjectorRegistration CreateTagProjector(
        bool isMultiProjector,
        Func<ITagStatePayload> initialState,
        Func<ITagStatePayload, Event, ITagStatePayload> applyEvent,
        Func<object, string> serializeState,
        Func<string, object> deserializeState) =>
        new(
            IsMultiProjector: isMultiProjector,
            CreateInitialState: () => initialState(),
            ApplyEvent: (state, ev, _) => applyEvent((ITagStatePayload)state, ev),
            SerializeState: serializeState,
            DeserializeState: deserializeState);

    private static ProjectorRegistration CreateMultiProjector<TProjector>(
        Func<TProjector> initialState,
        Func<TProjector, Event, List<ITag>, TProjector> applyEvent,
        JsonTypeInfo<TProjector> typeInfo)
        where TProjector : class =>
        CreateMultiProjector(
            initialState,
            applyEvent,
            state => JsonSerializer.Serialize((TProjector)state, typeInfo),
            json => string.IsNullOrWhiteSpace(json) || json == "{}"
                ? initialState()
                : JsonSerializer.Deserialize(json, typeInfo) ?? initialState());

    private static ProjectorRegistration CreateMultiProjector<TProjector>(
        Func<TProjector> initialState,
        Func<TProjector, Event, List<ITag>, TProjector> applyEvent,
        Func<object, string> serializeState,
        Func<string, object> deserializeState)
        where TProjector : class =>
        new(
            IsMultiProjector: true,
            CreateInitialState: () => initialState(),
            ApplyEvent: (state, ev, tags) => applyEvent((TProjector)state, ev, tags),
            SerializeState: serializeState,
            DeserializeState: deserializeState);

    private static QueryRegistration CreateSingleQuery<TProjector, TQuery, TOutput>(
        JsonTypeInfo<TQuery> queryInfo,
        JsonTypeInfo<TOutput> outputInfo,
        Func<TProjector, TQuery, IQueryContext, TOutput> execute)
        where TProjector : class
        where TQuery : class, new() =>
        new((state, instance, json) =>
        {
            TQuery query = string.IsNullOrWhiteSpace(json) || json == "{}"
                ? new TQuery()
                : JsonSerializer.Deserialize(json, queryInfo)
                    ?? new TQuery();
            var context = new WasmQueryContext(
                safeVersion: instance.Version,
                safeWindowThreshold: instance.LastSortableUniqueId,
                unsafeVersion: instance.Version);
            TOutput result = execute((TProjector)state, query, context);
            return JsonSerializer.Serialize(result, outputInfo);
        });

    private static QueryRegistration CreateListQuery<TProjector, TQuery, TOutput>(
        JsonTypeInfo<TQuery> queryInfo,
        JsonTypeInfo<List<TOutput>> itemsInfo,
        Func<TProjector, TQuery, IQueryContext, IEnumerable<TOutput>> filter,
        Func<IEnumerable<TOutput>, TQuery, IQueryContext, IEnumerable<TOutput>> sort)
        where TProjector : class
        where TQuery : class, new()
        where TOutput : notnull =>
        new((state, instance, json) =>
        {
            TQuery query = string.IsNullOrWhiteSpace(json) || json == "{}"
                ? new TQuery()
                : JsonSerializer.Deserialize(json, queryInfo)
                    ?? new TQuery();
            var context = new WasmQueryContext(
                safeVersion: instance.Version,
                safeWindowThreshold: instance.LastSortableUniqueId,
                unsafeVersion: instance.Version);
            List<TOutput> items = sort(filter((TProjector)state, query, context), query, context).ToList();
            ListQueryResult<TOutput> result = query is IQueryPagingParameter paging
                ? ListQueryResult<TOutput>.CreatePaginated(paging, items)
                : new ListQueryResult<TOutput>(items.Count, null, null, null, items);
            return SerializeListResult(result, itemsInfo);
        });

    private static string SerializeClassRoomState(object state)
    {
        if (state is EmptyTagStatePayload)
        {
            return "{}";
        }

        var snapshot = state switch
        {
            AvailableClassRoomState available => new ClassRoomProjectorSnapshot("available", available, null),
            FilledClassRoomState filled => new ClassRoomProjectorSnapshot("filled", null, filled),
            _ => new ClassRoomProjectorSnapshot("empty", null, null)
        };

        return JsonSerializer.Serialize(snapshot, WasmJsonContext.Default.ClassRoomProjectorSnapshot);
    }

    private static object DeserializeClassRoomState(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return new EmptyTagStatePayload();
        }

        ClassRoomProjectorSnapshot? snapshot = JsonSerializer.Deserialize(
            json,
            WasmJsonContext.Default.ClassRoomProjectorSnapshot);
        return snapshot?.StateKind switch
        {
            "available" when snapshot.AvailableState is not null => snapshot.AvailableState,
            "filled" when snapshot.FilledState is not null => snapshot.FilledState,
            _ => new EmptyTagStatePayload()
        };
    }

    private static string SerializeWeatherTagProjectionState(object state)
    {
        var projection = (WeatherForecastProjectorWithTagStateProjector)state;
        List<WeatherForecastTagStateSnapshot> snapshots = projection
            .GetCurrentTagStates()
            .Select(pair => new WeatherForecastTagStateSnapshot(
                pair.Key,
                (WeatherForecastState)pair.Value.Payload,
                pair.Value.Version,
                pair.Value.LastSortedUniqueId))
            .ToList();

        return JsonSerializer.Serialize(snapshots, WasmJsonContext.Default.ListWeatherForecastTagStateSnapshot);
    }

    private static object DeserializeWeatherTagProjectionState(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return WeatherForecastProjectorWithTagStateProjector.GenerateInitialPayload();
        }

        List<WeatherForecastTagStateSnapshot> snapshots = JsonSerializer.Deserialize(
                json,
                WasmJsonContext.Default.ListWeatherForecastTagStateSnapshot)
            ?? [];
        Dictionary<Guid, TagState> map = new();
        foreach (WeatherForecastTagStateSnapshot snapshot in snapshots)
        {
            TagStateId tagStateId = new(
                new WeatherForecastTag(snapshot.ForecastId),
                WeatherForecastProjector.ProjectorName);
            map[snapshot.ForecastId] = TagState.GetEmpty(tagStateId) with
            {
                Payload = snapshot.Payload,
                Version = snapshot.Version,
                LastSortedUniqueId = snapshot.LastSortableUniqueId ?? string.Empty,
                ProjectorVersion = WeatherForecastProjector.ProjectorVersion
            };
        }

        return new WeatherForecastProjectorWithTagStateProjector
        {
            State = SafeUnsafeProjectionState<Guid, TagState>.FromCurrentData(map)
        };
    }

    private static object DeserializeTagState<TState>(
        string json,
        Func<object> emptyFactory,
        JsonTypeInfo<TState> typeInfo)
        where TState : class =>
        string.IsNullOrWhiteSpace(json) || json == "{}"
            ? emptyFactory()
            : (JsonSerializer.Deserialize(json, typeInfo) as object) ?? emptyFactory();

    private static WasmEventMetadata ParseMetadata(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson) || metadataJson == "{}")
        {
            return new WasmEventMetadata([], null);
        }

        return JsonSerializer.Deserialize(metadataJson, WasmJsonContext.Default.WasmEventMetadata)
            ?? new WasmEventMetadata([], null);
    }

    private static List<ITag> ParseTags(IReadOnlyList<string> tagStrings)
    {
        List<ITag> tags = new(tagStrings.Count);
        foreach (string tagString in tagStrings)
        {
            string[] parts = tagString.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (TagReaders.TryGetValue(parts[0], out Func<string, ITag>? reader))
            {
                tags.Add(reader(parts[1]));
            }
        }

        return tags;
    }

    private static string SerializeListResult<T>(
        ListQueryResult<T> result,
        JsonTypeInfo<List<T>> itemsInfo)
        where T : notnull
    {
        string itemsJson = JsonSerializer.Serialize(result.Items.ToList(), itemsInfo);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        if (result.TotalCount is int totalCount)
        {
            writer.WriteNumber("totalCount", totalCount);
        }

        if (result.TotalPages is int totalPages)
        {
            writer.WriteNumber("totalPages", totalPages);
        }

        if (result.CurrentPage is int currentPage)
        {
            writer.WriteNumber("currentPage", currentPage);
        }

        if (result.PageSize is int pageSize)
        {
            writer.WriteNumber("pageSize", pageSize);
        }

        writer.WritePropertyName("items");
        using (JsonDocument document = JsonDocument.Parse(itemsJson))
        {
            document.RootElement.WriteTo(writer);
        }
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
