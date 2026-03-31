using Dcb.EventSource.MeetingRoom.ApprovalRequest;
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
///     Workflow for quick reservation: creates a draft, holds it, and confirms it in one step.
///     Use this for simple scenarios where no approval is needed.
/// </summary>
public class QuickReservationWorkflow(ISekibanExecutor executor)
{
    /// <summary>
    ///     Creates a reservation and immediately confirms it.
    /// </summary>
    /// <param name="roomId">The room to reserve</param>
    /// <param name="organizerId">The user making the reservation</param>
    /// <param name="startTime">Start time of the reservation</param>
    /// <param name="endTime">End time of the reservation</param>
    /// <param name="purpose">Purpose of the reservation</param>
    /// <param name="selectedEquipment">Optional list of room equipment to reserve for use</param>
    /// <param name="approvalRequestComment">Optional comment for approval request</param>
    /// <returns>The reservation result including ID and sortable unique ID</returns>
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

        var executionResult = await executor.ExecuteAsync(new CreateQuickReservation
        {
            ReservationId = reservationId,
            RoomId = roomId,
            OrganizerId = organizerId,
            OrganizerName = organizerName,
            StartTime = startTime,
            EndTime = endTime,
            Purpose = purpose,
            SelectedEquipment = selectedEquipment?.ToList() ?? [],
            RequiresApproval = false,
            ApprovalRequestId = null,
            ApprovalRequestComment = null
        });

        var sortableUniqueId = executionResult.SortableUniqueId ?? string.Empty;

        return new QuickReservationResult(reservationId, sortableUniqueId, false, null);
    }
}
