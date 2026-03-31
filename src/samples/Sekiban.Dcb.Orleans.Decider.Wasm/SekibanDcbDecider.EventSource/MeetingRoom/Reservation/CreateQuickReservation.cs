using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Dcb.EventSource.MeetingRoom.Room;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;

namespace Dcb.EventSource.MeetingRoom.Reservation;

public record CreateQuickReservation : ICommandWithHandler<CreateQuickReservation>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [Required]
    public Guid OrganizerId { get; init; }

    public string OrganizerName { get; init; } = string.Empty;

    [Required]
    public DateTime StartTime { get; init; }

    [Required]
    public DateTime EndTime { get; init; }

    [Required]
    [StringLength(500)]
    public string Purpose { get; init; } = string.Empty;

    public bool RequiresApproval { get; init; }

    public Guid? ApprovalRequestId { get; init; }

    public string? ApprovalRequestComment { get; init; }

    public List<string> SelectedEquipment { get; init; } = [];

    public static async Task<EventOrNone> HandleAsync(
        CreateQuickReservation command,
        ICommandContext context)
    {
        if (command.EndTime <= command.StartTime)
        {
            throw new ApplicationException("End time must be after start time");
        }

        if (command.StartTime < DateTime.UtcNow)
        {
            throw new ApplicationException("Cannot create reservation in the past");
        }

        var roomTag = new RoomTag(command.RoomId);
        if (!await context.TagExistsAsync(roomTag))
        {
            throw new ApplicationException($"Room {command.RoomId} not found");
        }

        var roomStateTyped = await context.GetStateAsync<RoomState, RoomProjector>(roomTag);
        if (roomStateTyped.Payload is not RoomState roomState || roomState.RoomId == Guid.Empty)
        {
            throw new ApplicationException($"Room {command.RoomId} not found");
        }

        var reservationTag = new ReservationTag(command.ReservationId);
        if (await context.TagExistsAsync(reservationTag))
        {
            throw new ApplicationException($"Reservation {command.ReservationId} already exists");
        }

        CreateReservationDraft.ValidateReservationMonth(command.StartTime, DateTime.UtcNow);
        var selectedEquipment = CreateReservationDraft.NormalizeSelectedEquipment(
            command.SelectedEquipment,
            roomState.Equipment);
        var requiresApproval = roomState.RequiresApproval;

        await context.AppendEvent(new ReservationDraftCreated(
            command.ReservationId,
            command.RoomId,
            command.OrganizerId,
            command.OrganizerName,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            selectedEquipment).GetEventWithTags());

        Guid? approvalRequestId = null;
        if (requiresApproval)
        {
            approvalRequestId = command.ApprovalRequestId;
            if (approvalRequestId is null)
            {
                throw new ApplicationException("Approval request is required for this room");
            }

            await context.AppendEvent(new ApprovalFlowStarted(
                approvalRequestId.Value,
                command.ReservationId,
                command.RoomId,
                command.OrganizerId,
                [],
                DateTime.UtcNow,
                command.ApprovalRequestComment).GetEventWithTags());
        }

        var holdEvent = new ReservationHoldCommitted(
            command.ReservationId,
            command.RoomId,
            command.OrganizerId,
            command.OrganizerName,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            requiresApproval,
            approvalRequestId,
            requiresApproval ? command.ApprovalRequestComment : null,
            selectedEquipment).GetEventWithTags();

        if (requiresApproval)
        {
            return await context.AppendEvent(holdEvent);
        }

        await context.AppendEvent(holdEvent);
        return await context.AppendEvent(new ReservationConfirmed(
            command.ReservationId,
            command.RoomId,
            command.OrganizerId,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            DateTime.UtcNow,
            null).GetEventWithTags());
    }
}
