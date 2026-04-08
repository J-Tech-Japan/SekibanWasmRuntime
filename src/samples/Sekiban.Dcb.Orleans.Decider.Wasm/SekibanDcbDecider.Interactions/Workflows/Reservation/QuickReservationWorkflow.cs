using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Sekiban.Dcb;

namespace Dcb.Interactions.Workflows.Reservation;

/// <summary>
///     Result of the quick reservation workflow.
/// </summary>
public record QuickReservationResult(
    Guid ReservationId,
    string SortableUniqueId,
    bool RequiresApproval,
    Guid? ApprovalRequestId);

/// <summary>
///     Workflow for quick reservation: creates a draft, holds it, and confirms it in one step.
///     Uses CreateQuickReservation which handles the entire lifecycle in a single command.
/// </summary>
public class QuickReservationWorkflow(ISekibanExecutor executor)
{
    public async Task<QuickReservationResult> ExecuteAsync(
        Guid roomId,
        Guid organizerId,
        string organizerName,
        DateTime startTime,
        DateTime endTime,
        string purpose,
        IReadOnlyList<string>? selectedEquipment = null,
        string? approvalRequestComment = null)
    {
        var reservationId = Guid.CreateVersion7();
        var approvalRequestId = Guid.CreateVersion7();

        var executionResult = await executor.ExecuteAsync(new CreateQuickReservation
        {
            ReservationId = reservationId,
            RoomId = roomId,
            ApprovalRequestId = approvalRequestId,
            OrganizerId = organizerId,
            OrganizerName = organizerName,
            StartTime = startTime,
            EndTime = endTime,
            Purpose = purpose,
            SelectedEquipment = selectedEquipment?.ToList() ?? [],
            ApprovalRequestComment = approvalRequestComment
        });
        var sortableUniqueId = executionResult.SortableUniqueId ?? string.Empty;
        var approvalStarted = executionResult.Events
            .Select(static writtenEvent => writtenEvent.Payload)
            .OfType<ApprovalFlowStarted>()
            .FirstOrDefault();

        return new QuickReservationResult(
            reservationId,
            sortableUniqueId,
            approvalStarted is not null,
            approvalStarted?.ApprovalRequestId);
    }
}
