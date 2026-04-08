using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record RejectReservation : ICommandWithHandler<RejectReservation>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [Required]
    public Guid ApprovalRequestId { get; init; }

    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public static async Task<EventOrNone> HandleAsync(
        RejectReservation command,
        ICommandContext context)
    {
        var tag = new ReservationTag(command.ReservationId);
        var exists = await context.TagExistsAsync(tag);

        if (!exists)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        var reservationState = await context.GetStateAsync<ReservationProjector>(tag);
        var payloadState = reservationState.Payload as ReservationState ?? ReservationState.Empty;
        var (roomId, organizerId, startTime) = GetReservationMetadata(payloadState);

        if (command.RoomId != roomId)
        {
            throw new ApplicationException(
                $"Reservation {command.ReservationId} belongs to room {roomId}, not {command.RoomId}");
        }

        var monthlyTag = UserMonthlyReservationTag.FromStartTime(organizerId, startTime);

        var payload = new ReservationRejected(
            command.ReservationId,
            roomId,
            command.ApprovalRequestId,
            command.Reason,
            DateTime.UtcNow);

        var tags = new List<ITag>
        {
            new ReservationTag(command.ReservationId),
            new RoomTag(roomId),
            monthlyTag
        };

        return new EventPayloadWithTags(payload, tags);
    }

    private static (Guid RoomId, Guid OrganizerId, DateTime StartTime) GetReservationMetadata(ReservationState state) =>
        state switch
        {
            ReservationState.ReservationDraft draft => (draft.RoomId, draft.OrganizerId, draft.StartTime),
            ReservationState.ReservationHeld held => (held.RoomId, held.OrganizerId, held.StartTime),
            ReservationState.ReservationConfirmed confirmed => (confirmed.RoomId, confirmed.OrganizerId, confirmed.StartTime),
            ReservationState.ReservationCancelled cancelled => (cancelled.RoomId, cancelled.OrganizerId, cancelled.StartTime),
            ReservationState.ReservationRejected rejected => (rejected.RoomId, rejected.OrganizerId, rejected.StartTime),
            _ => throw new ApplicationException("Reservation does not include organizer details.")
        };
}
