using System.Text.Json.Serialization;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.EquipmentItem;
using Dcb.MeetingRoomModels.Events.EquipmentReservation;
using Dcb.MeetingRoomModels.Events.EquipmentType;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.Events.UserAccess;
using Dcb.MeetingRoomModels.Events.UserDirectory;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.EquipmentType;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.States.UserMonthlyReservation;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.Domain;

// Weather types
[JsonSerializable(typeof(WeatherForecastCreated))]
[JsonSerializable(typeof(WeatherForecastLocationUpdated))]
[JsonSerializable(typeof(WeatherForecastDeleted))]
[JsonSerializable(typeof(CreateWeatherForecast))]
[JsonSerializable(typeof(UpdateWeatherForecastLocation))]
[JsonSerializable(typeof(DeleteWeatherForecast))]
[JsonSerializable(typeof(WeatherForecastState))]
[JsonSerializable(typeof(WeatherForecastItem))]
[JsonSerializable(typeof(WeatherForecastMultiProjection))]
[JsonSerializable(typeof(GetWeatherForecastListQuery))]
[JsonSerializable(typeof(Dictionary<string, WeatherForecastItem>))]
[JsonSerializable(typeof(List<WeatherForecastItem>))]
// ApprovalRequest events
[JsonSerializable(typeof(ApprovalFlowStarted))]
[JsonSerializable(typeof(ApprovalDecisionRecorded))]
// EquipmentItem events
[JsonSerializable(typeof(EquipmentItemRegistered))]
[JsonSerializable(typeof(EquipmentItemRetired))]
// EquipmentReservation events
[JsonSerializable(typeof(EquipmentCheckedOut))]
[JsonSerializable(typeof(EquipmentItemAssigned))]
[JsonSerializable(typeof(EquipmentReservationCancelled))]
[JsonSerializable(typeof(EquipmentReservationCreated))]
[JsonSerializable(typeof(EquipmentReturned))]
// EquipmentType events
[JsonSerializable(typeof(EquipmentTypeCreated))]
[JsonSerializable(typeof(EquipmentTypeUpdated))]
// Reservation events
[JsonSerializable(typeof(ReservationCancelled))]
[JsonSerializable(typeof(ReservationConfirmed))]
[JsonSerializable(typeof(ReservationDetailsUpdated))]
[JsonSerializable(typeof(ReservationDraftCreated))]
[JsonSerializable(typeof(ReservationExpiredCommitted))]
[JsonSerializable(typeof(ReservationHoldCommitted))]
[JsonSerializable(typeof(ReservationRejected))]
// Room events
[JsonSerializable(typeof(RoomCreated))]
[JsonSerializable(typeof(RoomDeactivated))]
[JsonSerializable(typeof(RoomReactivated))]
[JsonSerializable(typeof(RoomUpdated))]
// UserAccess events
[JsonSerializable(typeof(UserAccessDeactivated))]
[JsonSerializable(typeof(UserAccessGranted))]
[JsonSerializable(typeof(UserAccessReactivated))]
[JsonSerializable(typeof(UserRoleGranted))]
[JsonSerializable(typeof(UserRoleRevoked))]
// UserDirectory events
[JsonSerializable(typeof(ExternalIdentityLinked))]
[JsonSerializable(typeof(ExternalIdentityUnlinked))]
[JsonSerializable(typeof(UserDeactivated))]
[JsonSerializable(typeof(UserProfileUpdated))]
[JsonSerializable(typeof(UserReactivated))]
[JsonSerializable(typeof(UserRegistered))]
// State types - Room
[JsonSerializable(typeof(RoomState))]
// State types - Reservation (discriminated union)
[JsonSerializable(typeof(ReservationState))]
[JsonSerializable(typeof(ReservationState.ReservationEmpty))]
[JsonSerializable(typeof(ReservationState.ReservationDraft))]
[JsonSerializable(typeof(ReservationState.ReservationHeld))]
[JsonSerializable(typeof(ReservationState.ReservationConfirmed), TypeInfoPropertyName = "ReservationStateConfirmed")]
[JsonSerializable(typeof(ReservationState.ReservationCancelled), TypeInfoPropertyName = "ReservationStateCancelled")]
[JsonSerializable(typeof(ReservationState.ReservationRejected), TypeInfoPropertyName = "ReservationStateRejected")]
[JsonSerializable(typeof(ReservationState.ReservationExpired))]
// State types - ApprovalRequest (discriminated union)
[JsonSerializable(typeof(ApprovalRequestState))]
[JsonSerializable(typeof(ApprovalRequestState.ApprovalRequestEmpty))]
[JsonSerializable(typeof(ApprovalRequestState.ApprovalRequestPending))]
[JsonSerializable(typeof(ApprovalRequestState.ApprovalRequestApproved))]
[JsonSerializable(typeof(ApprovalRequestState.ApprovalRequestRejected))]
// State types - RoomReservations
[JsonSerializable(typeof(RoomReservationsState))]
[JsonSerializable(typeof(ReservationSlot))]
[JsonSerializable(typeof(Dictionary<Guid, ReservationSlot>))]
// State types - RoomDailyActivity
[JsonSerializable(typeof(RoomDailyActivityState))]
[JsonSerializable(typeof(ConfirmedTimeSlot))]
[JsonSerializable(typeof(Dictionary<Guid, ConfirmedTimeSlot>))]
// State types - UserAccess (discriminated union)
[JsonSerializable(typeof(UserAccessState))]
[JsonSerializable(typeof(UserAccessState.UserAccessEmpty))]
[JsonSerializable(typeof(UserAccessState.UserAccessActive))]
[JsonSerializable(typeof(UserAccessState.UserAccessDeactivated), TypeInfoPropertyName = "UserAccessStateDeactivated")]
// State types - UserDirectory (discriminated union)
[JsonSerializable(typeof(UserDirectoryState))]
[JsonSerializable(typeof(UserDirectoryState.UserDirectoryEmpty))]
[JsonSerializable(typeof(UserDirectoryState.UserDirectoryActive))]
[JsonSerializable(typeof(UserDirectoryState.UserDirectoryDeactivated))]
[JsonSerializable(typeof(ExternalIdentity))]
[JsonSerializable(typeof(List<ExternalIdentity>))]
// State types - UserMonthlyReservation
[JsonSerializable(typeof(UserMonthlyReservationState))]
// State types - EquipmentType (discriminated union)
[JsonSerializable(typeof(EquipmentTypeState))]
[JsonSerializable(typeof(EquipmentTypeState.EquipmentTypeEmpty))]
[JsonSerializable(typeof(EquipmentTypeState.EquipmentTypeActive))]
// Collection types needed for events
[JsonSerializable(typeof(List<Guid>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public partial class DomainJsonContext : JsonSerializerContext
{
}
