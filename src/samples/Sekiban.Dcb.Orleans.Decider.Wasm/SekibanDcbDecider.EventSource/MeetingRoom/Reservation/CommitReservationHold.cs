using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Dcb.EventSource.MeetingRoom.Room;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record CommitReservationHold : ICommandWithHandler<CommitReservationHold>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    public bool RequiresApproval { get; init; }

    public Guid? ApprovalRequestId { get; init; }

    public string? ApprovalRequestComment { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        CommitReservationHold command,
        ICommandContext context)
    {
        var tag = new ReservationTag(command.ReservationId);
        var exists = await context.TagExistsAsync(tag);

        if (!exists)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        var reservationStateTyped = await context.GetStateAsync<ReservationState, ReservationProjector>(tag);
        if (reservationStateTyped.Payload is not ReservationState.ReservationDraft draft)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} is not in draft state");
        }

        var roomStateTyped = await context.GetStateAsync<RoomState, RoomProjector>(new RoomTag(command.RoomId));
        if (roomStateTyped.Payload is not RoomState roomState || roomState.RoomId == Guid.Empty)
        {
            throw new ApplicationException($"Room {command.RoomId} not found");
        }

        var requiresApproval = roomState.RequiresApproval;
        var approvalRequestId = requiresApproval ? command.ApprovalRequestId : null;
        var approvalRequestComment = requiresApproval ? command.ApprovalRequestComment : null;

        if (requiresApproval && approvalRequestId == null)
        {
            throw new ApplicationException("Approval request is required for this room");
        }

        return new ReservationHoldCommitted(
            command.ReservationId,
            command.RoomId,
            draft.OrganizerId,
            draft.OrganizerName,
            draft.StartTime,
            draft.EndTime,
            draft.Purpose,
            requiresApproval,
            approvalRequestId,
            approvalRequestComment,
            draft.SelectedEquipment)
            .GetEventWithTags();
    }
}
