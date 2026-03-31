using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record ConfirmReservation : ICommandWithHandler<ConfirmReservation>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        ConfirmReservation command,
        ICommandContext context)
    {
        // 1. Get the reservation state to get full details
        var reservationTag = new ReservationTag(command.ReservationId);
        var reservationStateTyped = await context.GetStateAsync<ReservationState, ReservationProjector>(reservationTag);

        if (reservationStateTyped.Payload is ReservationState.ReservationEmpty)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        // 2. Extract reservation details
        if (reservationStateTyped.Payload is not ReservationState.ReservationHeld held)
        {
            throw new ApplicationException(
                $"Reservation {command.ReservationId} is in invalid state for confirmation: {reservationStateTyped.Payload.GetType().Name}");
        }

        var startTime = held.StartTime;
        var endTime = held.EndTime;
        var purpose = held.Purpose;
        var organizerId = held.OrganizerId;

        string? approvalDecisionComment = null;
        if (held.RequiresApproval)
        {
            if (held.ApprovalRequestId == null)
            {
                throw new ApplicationException("Approval request is required before confirmation");
            }

            var approvalTag = new ApprovalRequestTag(held.ApprovalRequestId.Value);
            var approvalStateTyped = await context.GetStateAsync<ApprovalRequestProjector>(approvalTag);
            if (approvalStateTyped.Payload is not ApprovalRequestState.ApprovalRequestApproved approved)
            {
                throw new ApplicationException("Reservation approval has not been granted");
            }

            approvalDecisionComment = approved.Comment;
        }

        // 3. Use room ID in conflict messages to keep command execution independent of room tag-state reads.
        var roomName = $"Room {command.RoomId}";

        // 4. Create confirmed event with all details for projectors.
        return new ReservationConfirmed(
            command.ReservationId,
            command.RoomId,
            organizerId,
            startTime,
            endTime,
            purpose,
            DateTime.UtcNow,
            approvalDecisionComment).GetEventWithTags();
    }
}
