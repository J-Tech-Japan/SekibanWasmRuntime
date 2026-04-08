using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record ExpireReservation : ICommandWithHandler<ExpireReservation>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [Required]
    [StringLength(500)]
    public string Reason { get; init; } = string.Empty;

    public Guid? PoolId { get; init; }

    public Guid? HoldId { get; init; }

    public Guid? ApprovalRequestId { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        ExpireReservation command,
        ICommandContext context)
    {
        var tag = new ReservationTag(command.ReservationId);
        var reservationStateTyped = await context.GetStateAsync<ReservationState, ReservationProjector>(tag);

        if (reservationStateTyped.Payload is ReservationState.ReservationEmpty)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        // Get time range from the current state
        var (roomId, startTime, endTime) = reservationStateTyped.Payload switch
        {
            ReservationState.ReservationHeld held => (held.RoomId, held.StartTime, held.EndTime),
            ReservationState.ReservationConfirmed confirmed => (confirmed.RoomId, confirmed.StartTime, confirmed.EndTime),
            ReservationState.ReservationDraft draft => (draft.RoomId, draft.StartTime, draft.EndTime),
            _ => throw new ApplicationException($"Reservation {command.ReservationId} cannot be expired from state: {reservationStateTyped.Payload.GetType().Name}")
        };

        if (command.RoomId != roomId)
        {
            throw new ApplicationException(
                $"Reservation {command.ReservationId} belongs to room {roomId}, not {command.RoomId}");
        }

        return new ReservationExpiredCommitted(
            command.ReservationId,
            roomId,
            startTime,
            endTime,
            DateTime.UtcNow,
            command.Reason,
            command.PoolId,
            command.HoldId,
            command.ApprovalRequestId)
            .GetEventWithTags();
    }
}
