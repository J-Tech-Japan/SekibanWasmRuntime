using Dcb.EventSource.MeetingRoom.Reservation;
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
///     Workflow for quick reservation: creates a draft, holds it, and confirms it
///     using three separate command executions to match the Rust/Go/TS client API behaviour.
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

        // Step 1: Create draft (separate command execution + commit)
        await executor.ExecuteAsync(new CreateReservationDraft
        {
            ReservationId = reservationId,
            RoomId = roomId,
            OrganizerId = organizerId,
            OrganizerName = organizerName,
            StartTime = startTime,
            EndTime = endTime,
            Purpose = purpose,
            SelectedEquipment = selectedEquipment?.ToList() ?? []
        });

        // Step 2: Commit hold (separate command execution + commit)
        var holdResult = await executor.ExecuteAsync(new CommitReservationHold
        {
            ReservationId = reservationId,
            RoomId = roomId,
            RequiresApproval = false,
            ApprovalRequestId = null,
            ApprovalRequestComment = approvalRequestComment
        });

        // Step 3: Confirm (separate command execution + commit)
        var confirmResult = await executor.ExecuteAsync(new ConfirmReservation
        {
            ReservationId = reservationId,
            RoomId = roomId
        });

        var sortableUniqueId = confirmResult.SortableUniqueId ?? holdResult.SortableUniqueId ?? string.Empty;

        return new QuickReservationResult(
            reservationId,
            sortableUniqueId,
            false,
            null);
    }
}
