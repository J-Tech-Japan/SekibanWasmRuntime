using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.Tags;
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

        var selectedEquipment = command.SelectedEquipment
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await context.AppendEvent(new ReservationDraftCreated(
            command.ReservationId,
            command.RoomId,
            command.OrganizerId,
            command.OrganizerName,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            selectedEquipment).GetEventWithTags());

        var holdEvent = new ReservationHoldCommitted(
            command.ReservationId,
            command.RoomId,
            command.OrganizerId,
            command.OrganizerName,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            command.RequiresApproval,
            command.RequiresApproval ? command.ApprovalRequestId : null,
            command.RequiresApproval ? command.ApprovalRequestComment : null,
            selectedEquipment).GetEventWithTags();

        if (command.RequiresApproval)
        {
            if (command.ApprovalRequestId is null)
            {
                throw new ApplicationException("Approval request is required for this room");
            }

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
