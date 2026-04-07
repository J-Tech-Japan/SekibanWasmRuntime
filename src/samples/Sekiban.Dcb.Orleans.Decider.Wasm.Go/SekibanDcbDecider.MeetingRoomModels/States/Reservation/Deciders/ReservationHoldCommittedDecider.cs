using Dcb.MeetingRoomModels.Events.Reservation;
namespace Dcb.MeetingRoomModels.States.Reservation.Deciders;

/// <summary>
///     Decider for ReservationHoldCommitted event
/// </summary>
public static class ReservationHoldCommittedDecider
{
    public static Func<DateTime> UtcNowProvider { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    ///     Validate preconditions for committing hold
    /// </summary>
    public static void Validate(this ReservationState.ReservationDraft state)
    {
        var now = UtcNowProvider();
        if (state.StartTime <= now)
        {
            throw new InvalidOperationException("Cannot commit hold for past or current time");
        }

        if (state.EndTime <= state.StartTime)
        {
            throw new InvalidOperationException("End time must be after start time");
        }
    }

    /// <summary>
    ///     Apply ReservationHoldCommitted event to ReservationState
    /// </summary>
    public static ReservationState Evolve(this ReservationState state, ReservationHoldCommitted committed) =>
        state switch
        {
            ReservationState.ReservationDraft _ => new ReservationState.ReservationHeld(
                committed.ReservationId,
                committed.RoomId,
                committed.OrganizerId,
                committed.OrganizerName,
                committed.StartTime,
                committed.EndTime,
                committed.Purpose,
                committed.SelectedEquipment ?? [],
                committed.RequiresApproval,
                committed.ApprovalRequestId,
                committed.ApprovalRequestComment),
            _ => state // Idempotency: ignore if already committed
        };
}
