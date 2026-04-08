using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record UpdateReservationDetails : ICommandWithHandler<UpdateReservationDetails>
{
    [Required]
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [StringLength(200)]
    public string? Title { get; init; }

    [StringLength(1000)]
    public string? Description { get; init; }

    [Range(1, 1000)]
    public int? AttendeeCount { get; init; }

    public bool? HasExternalGuests { get; init; }

    public Dictionary<string, int>? RequiredEquipment { get; init; }

    public static async Task<EventOrNone> HandleAsync(
        UpdateReservationDetails command,
        ICommandContext context)
    {
        var tag = new ReservationTag(command.ReservationId);
        var reservationStateTyped = await context.GetStateAsync<ReservationState, ReservationProjector>(tag);
        var roomId = reservationStateTyped.Payload switch
        {
            ReservationState.ReservationDraft draft => draft.RoomId,
            ReservationState.ReservationHeld held => held.RoomId,
            ReservationState.ReservationConfirmed confirmed => confirmed.RoomId,
            ReservationState.ReservationCancelled cancelled => cancelled.RoomId,
            ReservationState.ReservationRejected rejected => rejected.RoomId,
            ReservationState.ReservationExpired expired => expired.RoomId,
            ReservationState.ReservationEmpty => Guid.Empty,
            _ => Guid.Empty
        };

        if (roomId == Guid.Empty)
        {
            throw new ApplicationException($"Reservation {command.ReservationId} not found");
        }

        if (command.RoomId != roomId)
        {
            throw new ApplicationException(
                $"Reservation {command.ReservationId} belongs to room {roomId}, not {command.RoomId}");
        }

        return new ReservationDetailsUpdated(
            command.ReservationId,
            roomId,
            command.Title,
            command.Description,
            command.AttendeeCount,
            command.HasExternalGuests,
            command.RequiredEquipment)
            .GetEventWithTags();
    }
}
