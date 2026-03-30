using System.Text.Json.Serialization;
using Dcb.EventSource.ClassRoom;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.Projections;
using Dcb.EventSource.Queries;
using Dcb.EventSource.Student;
using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.Events.Student;
using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.States.Weather;
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

namespace SekibanDcbDecider.Wasm;

[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(WasmEventMetadata))]
[JsonSerializable(typeof(ClassRoomCreated))]
[JsonSerializable(typeof(StudentDroppedFromClassRoom))]
[JsonSerializable(typeof(StudentEnrolledInClassRoom))]
[JsonSerializable(typeof(StudentCreated))]
[JsonSerializable(typeof(LocationNameChanged))]
[JsonSerializable(typeof(WeatherForecastCreated))]
[JsonSerializable(typeof(WeatherForecastDeleted))]
[JsonSerializable(typeof(WeatherForecastUpdated))]
[JsonSerializable(typeof(ApprovalDecisionRecorded))]
[JsonSerializable(typeof(ApprovalFlowStarted))]
[JsonSerializable(typeof(EquipmentItemRegistered))]
[JsonSerializable(typeof(EquipmentItemRetired))]
[JsonSerializable(typeof(EquipmentCheckedOut))]
[JsonSerializable(typeof(EquipmentItemAssigned))]
[JsonSerializable(typeof(EquipmentReservationCancelled))]
[JsonSerializable(typeof(EquipmentReservationCreated))]
[JsonSerializable(typeof(EquipmentReturned))]
[JsonSerializable(typeof(EquipmentTypeCreated))]
[JsonSerializable(typeof(EquipmentTypeUpdated))]
[JsonSerializable(typeof(ReservationCancelled), TypeInfoPropertyName = "ReservationCancelledEvent")]
[JsonSerializable(typeof(ReservationConfirmed), TypeInfoPropertyName = "ReservationConfirmedEvent")]
[JsonSerializable(typeof(ReservationDetailsUpdated))]
[JsonSerializable(typeof(ReservationDraftCreated))]
[JsonSerializable(typeof(ReservationExpiredCommitted))]
[JsonSerializable(typeof(ReservationHoldCommitted))]
[JsonSerializable(typeof(ReservationRejected), TypeInfoPropertyName = "ReservationRejectedEvent")]
[JsonSerializable(typeof(RoomCreated))]
[JsonSerializable(typeof(RoomDeactivated))]
[JsonSerializable(typeof(RoomReactivated))]
[JsonSerializable(typeof(RoomUpdated))]
[JsonSerializable(typeof(UserAccessDeactivated), TypeInfoPropertyName = "UserAccessDeactivatedEvent")]
[JsonSerializable(typeof(UserAccessGranted))]
[JsonSerializable(typeof(UserAccessReactivated))]
[JsonSerializable(typeof(UserRoleGranted))]
[JsonSerializable(typeof(UserRoleRevoked))]
[JsonSerializable(typeof(ExternalIdentityLinked))]
[JsonSerializable(typeof(ExternalIdentityUnlinked))]
[JsonSerializable(typeof(UserDeactivated))]
[JsonSerializable(typeof(UserProfileUpdated))]
[JsonSerializable(typeof(UserReactivated))]
[JsonSerializable(typeof(UserRegistered))]
[JsonSerializable(typeof(StudentState))]
[JsonSerializable(typeof(AvailableClassRoomState))]
[JsonSerializable(typeof(FilledClassRoomState))]
[JsonSerializable(typeof(WeatherForecastState))]
[JsonSerializable(typeof(ApprovalRequestState))]
[JsonSerializable(typeof(UserMonthlyReservationState))]
[JsonSerializable(typeof(EquipmentItemState))]
[JsonSerializable(typeof(EquipmentTypeState))]
[JsonSerializable(typeof(EquipmentReservationState))]
[JsonSerializable(typeof(RoomState))]
[JsonSerializable(typeof(UserAccessState))]
[JsonSerializable(typeof(ReservationState))]
[JsonSerializable(typeof(RoomDailyActivityState))]
[JsonSerializable(typeof(RoomReservationsState))]
[JsonSerializable(typeof(UserDirectoryState))]
[JsonSerializable(typeof(ExternalIdentity))]
[JsonSerializable(typeof(StudentListProjection))]
[JsonSerializable(typeof(ClassRoomListProjection))]
[JsonSerializable(typeof(WeatherForecastProjection))]
[JsonSerializable(typeof(WeatherForecastProjectorWithTagStateProjector))]
[JsonSerializable(typeof(ApprovalRequestListProjection))]
[JsonSerializable(typeof(EquipmentTypeListProjection))]
[JsonSerializable(typeof(ReservationListProjection))]
[JsonSerializable(typeof(RoomListProjection))]
[JsonSerializable(typeof(StudentSummaries))]
[JsonSerializable(typeof(StudentSummaries.Item))]
[JsonSerializable(typeof(UserAccessListProjection))]
[JsonSerializable(typeof(UserDirectoryListProjection))]
[JsonSerializable(typeof(GetApprovalInboxQuery))]
[JsonSerializable(typeof(GetEquipmentTypeListQuery))]
[JsonSerializable(typeof(GetReservationListQuery))]
[JsonSerializable(typeof(GetRoomListQuery))]
[JsonSerializable(typeof(GetUserAccessListQuery))]
[JsonSerializable(typeof(GetUserDirectoryListQuery))]
[JsonSerializable(typeof(GetClassRoomListQuery))]
[JsonSerializable(typeof(GetStudentListQuery))]
[JsonSerializable(typeof(GetWeatherForecastCountGenericQuery))]
[JsonSerializable(typeof(GetWeatherForecastCountQuery))]
[JsonSerializable(typeof(GetWeatherForecastCountSingleQuery))]
[JsonSerializable(typeof(GetWeatherForecastListGenericQuery))]
[JsonSerializable(typeof(GetWeatherForecastListQuery))]
[JsonSerializable(typeof(GetWeatherForecastListSingleQuery))]
[JsonSerializable(typeof(ApprovalInboxItem))]
[JsonSerializable(typeof(EquipmentTypeListItem))]
[JsonSerializable(typeof(ReservationListItem))]
[JsonSerializable(typeof(RoomListItem))]
[JsonSerializable(typeof(UserAccessListItem))]
[JsonSerializable(typeof(UserDirectoryListItem))]
[JsonSerializable(typeof(ClassRoomItem))]
[JsonSerializable(typeof(WeatherForecastItem))]
[JsonSerializable(typeof(WeatherForecastCountResult))]
[JsonSerializable(typeof(List<ApprovalInboxItem>))]
[JsonSerializable(typeof(List<EquipmentTypeListItem>))]
[JsonSerializable(typeof(List<ReservationListItem>))]
[JsonSerializable(typeof(List<RoomListItem>))]
[JsonSerializable(typeof(List<UserAccessListItem>))]
[JsonSerializable(typeof(List<UserDirectoryListItem>))]
[JsonSerializable(typeof(List<ClassRoomItem>))]
[JsonSerializable(typeof(List<StudentState>))]
[JsonSerializable(typeof(List<WeatherForecastItem>))]
[JsonSerializable(typeof(Dictionary<Guid, StudentSummaries.Item>))]
[JsonSerializable(typeof(ClassRoomProjectorSnapshot))]
[JsonSerializable(typeof(WeatherForecastTagStateSnapshot))]
[JsonSerializable(typeof(List<WeatherForecastTagStateSnapshot>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public partial class WasmJsonContext : JsonSerializerContext
{
}

public sealed record WasmEventMetadata(string[]? Tags, string? SortableUniqueId);

public sealed record ClassRoomProjectorSnapshot(
    string StateKind,
    AvailableClassRoomState? AvailableState,
    FilledClassRoomState? FilledState);

public sealed record WeatherForecastTagStateSnapshot(
    Guid ForecastId,
    WeatherForecastState Payload,
    int Version,
    string? LastSortableUniqueId);
