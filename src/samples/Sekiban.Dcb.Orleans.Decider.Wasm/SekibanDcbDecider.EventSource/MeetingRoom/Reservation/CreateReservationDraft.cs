using Dcb.MeetingRoomModels.Events.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
using System.Linq;
namespace Dcb.EventSource.MeetingRoom.Reservation;

public record CreateReservationDraft : ICommandWithHandler<CreateReservationDraft>
{
    public Guid ReservationId { get; init; }

    [Required]
    public Guid RoomId { get; init; }

    [Required]
    public Guid OrganizerId { get; init; }

    public string? OrganizerName { get; init; }

    [Required]
    public DateTime StartTime { get; init; }

    [Required]
    public DateTime EndTime { get; init; }

    [Required]
    [StringLength(500)]
    public string Purpose { get; init; } = string.Empty;

    public List<string> SelectedEquipment { get; init; } = [];

    public static async Task<EventOrNone> HandleAsync(
        CreateReservationDraft command,
        ICommandContext context)
    {
        var reservationId = command.ReservationId != Guid.Empty ? command.ReservationId : Guid.CreateVersion7();

        // Verify the room exists.
        var roomTag = new RoomTag(command.RoomId);
        var roomExists = await context.TagExistsAsync(roomTag);

        if (!roomExists)
        {
            throw new ApplicationException($"Room {command.RoomId} not found");
        }

        // Verify the reservation doesn't already exist
        var reservationTag = new ReservationTag(reservationId);
        var exists = await context.TagExistsAsync(reservationTag);
        if (exists)
        {
            throw new ApplicationException($"Reservation {reservationId} already exists");
        }

        // Validate times
        if (command.EndTime <= command.StartTime)
        {
            throw new ApplicationException("End time must be after start time");
        }

        if (command.StartTime < DateTime.UtcNow)
        {
            throw new ApplicationException("Cannot create reservation in the past");
        }

        ValidateReservationMonth(command.StartTime, DateTime.UtcNow);

        var selectedEquipment = NormalizeSelectedEquipment(command.SelectedEquipment);

        return new ReservationDraftCreated(
            reservationId,
            command.RoomId,
            command.OrganizerId,
            command.OrganizerName ?? string.Empty,
            command.StartTime,
            command.EndTime,
            command.Purpose,
            selectedEquipment)
            .GetEventWithTags();
    }

    private static void ValidateReservationMonth(DateTime startTime, DateTime nowUtc)
    {
        var startMonth = new DateOnly(startTime.Year, startTime.Month, 1);
        var currentMonth = new DateOnly(nowUtc.Year, nowUtc.Month, 1);
        var nextMonth = currentMonth.AddMonths(1);

        if (startMonth != currentMonth && startMonth != nextMonth)
        {
            throw new ApplicationException("Reservations can only be made for this month or next month.");
        }
    }

    private static List<string> NormalizeSelectedEquipment(List<string> selectedEquipment)
    {
        if (selectedEquipment.Count == 0)
        {
            return [];
        }

        var trimmedSelections = selectedEquipment
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();

        if (trimmedSelections.Count == 0)
        {
            return [];
        }

        return trimmedSelections
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
