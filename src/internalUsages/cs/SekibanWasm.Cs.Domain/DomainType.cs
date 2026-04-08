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
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.Domain;

public static class DomainType
{
    private static readonly Lazy<DcbDomainTypes> RuntimeDomainTypes = new(CreateRuntimeDomainTypes);
    private static readonly Lazy<DcbDomainTypes> WasmDomainTypes = new(CreateWasmDomainTypes);

    public static DcbDomainTypes GetDomainTypes() => RuntimeDomainTypes.Value;

    public static DcbDomainTypes GetWasmDomainTypes() => WasmDomainTypes.Value;

    private static DcbDomainTypes CreateRuntimeDomainTypes() =>
        DcbDomainTypesExtensions.Simple(types =>
        {
            RegisterCommonRuntimeTypes(types);
            types.MultiProjectorTypes.RegisterProjector<WeatherForecastMultiProjection>();
            types.QueryTypes.RegisterListQuery<GetWeatherForecastListQuery>();
        });

    private static DcbDomainTypes CreateWasmDomainTypes()
    {
        var builder = new AotDomainTypesBuilder(DomainJsonContext.Default.Options);
        RegisterCommonAotTypes(builder);
        return builder.Build();
    }

    private static void RegisterCommonRuntimeTypes(DcbDomainTypesExtensions.Builder types)
    {
        // Weather events
        types.EventTypes.RegisterEventType<WeatherForecastCreated>();
        types.EventTypes.RegisterEventType<WeatherForecastLocationUpdated>();
        types.EventTypes.RegisterEventType<WeatherForecastDeleted>();

        // ApprovalRequest events
        types.EventTypes.RegisterEventType<ApprovalFlowStarted>();
        types.EventTypes.RegisterEventType<ApprovalDecisionRecorded>();

        // EquipmentItem events
        types.EventTypes.RegisterEventType<EquipmentItemRegistered>();
        types.EventTypes.RegisterEventType<EquipmentItemRetired>();

        // EquipmentReservation events
        types.EventTypes.RegisterEventType<EquipmentCheckedOut>();
        types.EventTypes.RegisterEventType<EquipmentItemAssigned>();
        types.EventTypes.RegisterEventType<EquipmentReservationCancelled>();
        types.EventTypes.RegisterEventType<EquipmentReservationCreated>();
        types.EventTypes.RegisterEventType<EquipmentReturned>();

        // EquipmentType events
        types.EventTypes.RegisterEventType<EquipmentTypeCreated>();
        types.EventTypes.RegisterEventType<EquipmentTypeUpdated>();

        // Reservation events
        types.EventTypes.RegisterEventType<ReservationCancelled>();
        types.EventTypes.RegisterEventType<ReservationConfirmed>();
        types.EventTypes.RegisterEventType<ReservationDetailsUpdated>();
        types.EventTypes.RegisterEventType<ReservationDraftCreated>();
        types.EventTypes.RegisterEventType<ReservationExpiredCommitted>();
        types.EventTypes.RegisterEventType<ReservationHoldCommitted>();
        types.EventTypes.RegisterEventType<ReservationRejected>();

        // Room events
        types.EventTypes.RegisterEventType<RoomCreated>();
        types.EventTypes.RegisterEventType<RoomDeactivated>();
        types.EventTypes.RegisterEventType<RoomReactivated>();
        types.EventTypes.RegisterEventType<RoomUpdated>();

        // UserAccess events
        types.EventTypes.RegisterEventType<UserAccessDeactivated>();
        types.EventTypes.RegisterEventType<UserAccessGranted>();
        types.EventTypes.RegisterEventType<UserAccessReactivated>();
        types.EventTypes.RegisterEventType<UserRoleGranted>();
        types.EventTypes.RegisterEventType<UserRoleRevoked>();

        // UserDirectory events
        types.EventTypes.RegisterEventType<ExternalIdentityLinked>();
        types.EventTypes.RegisterEventType<ExternalIdentityUnlinked>();
        types.EventTypes.RegisterEventType<UserDeactivated>();
        types.EventTypes.RegisterEventType<UserProfileUpdated>();
        types.EventTypes.RegisterEventType<UserReactivated>();
        types.EventTypes.RegisterEventType<UserRegistered>();

        // Weather tag projector
        types.TagProjectorTypes.RegisterProjector<WeatherForecastProjector>();
        types.TagStatePayloadTypes.RegisterPayloadType<WeatherForecastState>();
        types.TagTypes.RegisterTagGroupType<WeatherForecastTag>();

        // MeetingRoom tag state payload types
        types.TagStatePayloadTypes.RegisterPayloadType<RoomState>();
        types.TagStatePayloadTypes.RegisterPayloadType<ReservationState>();
        types.TagStatePayloadTypes.RegisterPayloadType<ApprovalRequestState>();
        types.TagStatePayloadTypes.RegisterPayloadType<RoomReservationsState>();
        types.TagStatePayloadTypes.RegisterPayloadType<RoomDailyActivityState>();
        types.TagStatePayloadTypes.RegisterPayloadType<UserAccessState>();
        types.TagStatePayloadTypes.RegisterPayloadType<UserDirectoryState>();
        types.TagStatePayloadTypes.RegisterPayloadType<UserMonthlyReservationState>();
        types.TagStatePayloadTypes.RegisterPayloadType<EquipmentTypeState>();

        // MeetingRoom tag types
        types.TagTypes.RegisterTagGroupType<RoomTag>();
        types.TagTypes.RegisterTagGroupType<ReservationTag>();
        types.TagTypes.RegisterTagGroupType<ApprovalRequestTag>();
        types.TagTypes.RegisterTagGroupType<RoomDailyActivityTag>();
        types.TagTypes.RegisterTagGroupType<UserAccessTag>();
        types.TagTypes.RegisterTagGroupType<UserTag>();
        types.TagTypes.RegisterTagGroupType<UserMonthlyReservationTag>();
        types.TagTypes.RegisterTagGroupType<EquipmentTypeTag>();
    }

    private static void RegisterCommonAotTypes(AotDomainTypesBuilder builder)
    {
        // Weather events
        builder.EventTypes.Register(
            nameof(WeatherForecastCreated),
            DomainJsonContext.Default.WeatherForecastCreated);
        builder.EventTypes.Register(
            nameof(WeatherForecastLocationUpdated),
            DomainJsonContext.Default.WeatherForecastLocationUpdated);
        builder.EventTypes.Register(
            nameof(WeatherForecastDeleted),
            DomainJsonContext.Default.WeatherForecastDeleted);

        // ApprovalRequest events
        builder.EventTypes.Register(
            nameof(ApprovalFlowStarted),
            DomainJsonContext.Default.ApprovalFlowStarted);
        builder.EventTypes.Register(
            nameof(ApprovalDecisionRecorded),
            DomainJsonContext.Default.ApprovalDecisionRecorded);

        // EquipmentItem events
        builder.EventTypes.Register(
            nameof(EquipmentItemRegistered),
            DomainJsonContext.Default.EquipmentItemRegistered);
        builder.EventTypes.Register(
            nameof(EquipmentItemRetired),
            DomainJsonContext.Default.EquipmentItemRetired);

        // EquipmentReservation events
        builder.EventTypes.Register(
            nameof(EquipmentCheckedOut),
            DomainJsonContext.Default.EquipmentCheckedOut);
        builder.EventTypes.Register(
            nameof(EquipmentItemAssigned),
            DomainJsonContext.Default.EquipmentItemAssigned);
        builder.EventTypes.Register(
            nameof(EquipmentReservationCancelled),
            DomainJsonContext.Default.EquipmentReservationCancelled);
        builder.EventTypes.Register(
            nameof(EquipmentReservationCreated),
            DomainJsonContext.Default.EquipmentReservationCreated);
        builder.EventTypes.Register(
            nameof(EquipmentReturned),
            DomainJsonContext.Default.EquipmentReturned);

        // EquipmentType events
        builder.EventTypes.Register(
            nameof(EquipmentTypeCreated),
            DomainJsonContext.Default.EquipmentTypeCreated);
        builder.EventTypes.Register(
            nameof(EquipmentTypeUpdated),
            DomainJsonContext.Default.EquipmentTypeUpdated);

        // Reservation events
        builder.EventTypes.Register(
            nameof(ReservationCancelled),
            DomainJsonContext.Default.ReservationCancelled);
        builder.EventTypes.Register(
            nameof(ReservationConfirmed),
            DomainJsonContext.Default.ReservationConfirmed);
        builder.EventTypes.Register(
            nameof(ReservationDetailsUpdated),
            DomainJsonContext.Default.ReservationDetailsUpdated);
        builder.EventTypes.Register(
            nameof(ReservationDraftCreated),
            DomainJsonContext.Default.ReservationDraftCreated);
        builder.EventTypes.Register(
            nameof(ReservationExpiredCommitted),
            DomainJsonContext.Default.ReservationExpiredCommitted);
        builder.EventTypes.Register(
            nameof(ReservationHoldCommitted),
            DomainJsonContext.Default.ReservationHoldCommitted);
        builder.EventTypes.Register(
            nameof(ReservationRejected),
            DomainJsonContext.Default.ReservationRejected);

        // Room events
        builder.EventTypes.Register(
            nameof(RoomCreated),
            DomainJsonContext.Default.RoomCreated);
        builder.EventTypes.Register(
            nameof(RoomDeactivated),
            DomainJsonContext.Default.RoomDeactivated);
        builder.EventTypes.Register(
            nameof(RoomReactivated),
            DomainJsonContext.Default.RoomReactivated);
        builder.EventTypes.Register(
            nameof(RoomUpdated),
            DomainJsonContext.Default.RoomUpdated);

        // UserAccess events
        builder.EventTypes.Register(
            nameof(UserAccessDeactivated),
            DomainJsonContext.Default.UserAccessDeactivated);
        builder.EventTypes.Register(
            nameof(UserAccessGranted),
            DomainJsonContext.Default.UserAccessGranted);
        builder.EventTypes.Register(
            nameof(UserAccessReactivated),
            DomainJsonContext.Default.UserAccessReactivated);
        builder.EventTypes.Register(
            nameof(UserRoleGranted),
            DomainJsonContext.Default.UserRoleGranted);
        builder.EventTypes.Register(
            nameof(UserRoleRevoked),
            DomainJsonContext.Default.UserRoleRevoked);

        // UserDirectory events
        builder.EventTypes.Register(
            nameof(ExternalIdentityLinked),
            DomainJsonContext.Default.ExternalIdentityLinked);
        builder.EventTypes.Register(
            nameof(ExternalIdentityUnlinked),
            DomainJsonContext.Default.ExternalIdentityUnlinked);
        builder.EventTypes.Register(
            nameof(UserDeactivated),
            DomainJsonContext.Default.UserDeactivated);
        builder.EventTypes.Register(
            nameof(UserProfileUpdated),
            DomainJsonContext.Default.UserProfileUpdated);
        builder.EventTypes.Register(
            nameof(UserReactivated),
            DomainJsonContext.Default.UserReactivated);
        builder.EventTypes.Register(
            nameof(UserRegistered),
            DomainJsonContext.Default.UserRegistered);

        // Weather tag projector + state + tag
        builder.TagProjectorTypes.RegisterProjector<WeatherForecastProjector>();
        builder.TagStatePayloadTypes.Register(
            nameof(WeatherForecastState),
            DomainJsonContext.Default.WeatherForecastState);
        builder.TagTypes.RegisterTagGroupType<WeatherForecastTag>();

        // MeetingRoom tag state payload types
        builder.TagStatePayloadTypes.Register(
            nameof(RoomState),
            DomainJsonContext.Default.RoomState);
        builder.TagStatePayloadTypes.Register(
            nameof(ReservationState),
            DomainJsonContext.Default.ReservationState);
        builder.TagStatePayloadTypes.Register(
            nameof(ApprovalRequestState),
            DomainJsonContext.Default.ApprovalRequestState);
        builder.TagStatePayloadTypes.Register(
            nameof(RoomReservationsState),
            DomainJsonContext.Default.RoomReservationsState);
        builder.TagStatePayloadTypes.Register(
            nameof(RoomDailyActivityState),
            DomainJsonContext.Default.RoomDailyActivityState);
        builder.TagStatePayloadTypes.Register(
            nameof(UserAccessState),
            DomainJsonContext.Default.UserAccessState);
        builder.TagStatePayloadTypes.Register(
            nameof(UserDirectoryState),
            DomainJsonContext.Default.UserDirectoryState);
        builder.TagStatePayloadTypes.Register(
            nameof(UserMonthlyReservationState),
            DomainJsonContext.Default.UserMonthlyReservationState);
        builder.TagStatePayloadTypes.Register(
            nameof(EquipmentTypeState),
            DomainJsonContext.Default.EquipmentTypeState);

        // MeetingRoom tag types
        builder.TagTypes.RegisterTagGroupType<RoomTag>();
        builder.TagTypes.RegisterTagGroupType<ReservationTag>();
        builder.TagTypes.RegisterTagGroupType<ApprovalRequestTag>();
        builder.TagTypes.RegisterTagGroupType<RoomDailyActivityTag>();
        builder.TagTypes.RegisterTagGroupType<UserAccessTag>();
        builder.TagTypes.RegisterTagGroupType<UserTag>();
        builder.TagTypes.RegisterTagGroupType<UserMonthlyReservationTag>();
        builder.TagTypes.RegisterTagGroupType<EquipmentTypeTag>();
    }
}
