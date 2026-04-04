using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.User;
using Dcb.Interactions.Workflows.Reservation;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;

namespace SekibanDcbDecider.ClientApi.Endpoints;

public static class TestDataEndpoints
{
    public static void MapTestDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/test-data")
            .WithTags("Test Data")
            .RequireAuthorization();

        group.MapPost("/generate", GenerateTestDataAsync)
            .WithName("GenerateTestData");

        group.MapPost("/generate-rooms", GenerateRoomsAsync)
            .WithName("GenerateRooms");

        group.MapPost("/generate-reservations", GenerateReservationsAsync)
            .WithName("GenerateReservations");
    }

    private static async Task<IResult> GenerateTestDataAsync(
        [FromQuery] int? timeZoneOffsetMinutes,
        [FromServices] ISekibanExecutor executor,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("TestDataEndpoints");
        var result = new TestDataGenerationResult();

        // Generate sample user first
        var user = await GenerateUserInternalAsync(executor, logger);
        result.UserId = user.UserId;
        result.UserName = user.DisplayName;

        // Generate rooms
        var rooms = await GenerateRoomsInternalAsync(executor, logger);
        result.RoomsCreated = rooms.RoomIds.Count;
        result.RoomIds = rooms.RoomIds;
        result.SortableUniqueId = rooms.LastSortableUniqueId;
        await WaitForRoomsVisibleAsync(executor, rooms.RoomIds, rooms.LastSortableUniqueId, logger);

        // Generate reservations using the created rooms and user
        if (rooms.RoomIds.Count > 0)
        {
            var reservations = await GenerateReservationsInternalAsync(
                executor,
                rooms.RoomIds,
                user.UserId,
                user.DisplayName,
                timeZoneOffsetMinutes,
                logger);
            result.ReservationsCreated = reservations.ReservationIds.Count;
            result.ReservationIds = reservations.ReservationIds;
            result.Errors = reservations.Errors;
            result.SortableUniqueId = reservations.LastSortableUniqueId ?? result.SortableUniqueId;
            await WaitForReservationsVisibleAsync(
                executor,
                reservations.ReservationIds,
                reservations.LastSortableUniqueId,
                logger);
        }

        return Results.Ok(result);
    }

    private static async Task<IResult> GenerateRoomsAsync(
        [FromServices] ISekibanExecutor executor,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("TestDataEndpoints");
        var rooms = await GenerateRoomsInternalAsync(executor, logger);
        await WaitForRoomsVisibleAsync(executor, rooms.RoomIds, rooms.LastSortableUniqueId, logger);
        return Results.Ok(new
        {
            roomsCreated = rooms.RoomIds.Count,
            roomIds = rooms.RoomIds,
            sortableUniqueId = rooms.LastSortableUniqueId
        });
    }

    private static async Task<IResult> GenerateReservationsAsync(
        [FromQuery] Guid? roomId,
        [FromQuery] int? timeZoneOffsetMinutes,
        [FromServices] ISekibanExecutor executor,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("TestDataEndpoints");

        // Generate sample user first
        var user = await GenerateUserInternalAsync(executor, logger);

        // If no roomId provided, we need at least one room
        List<Guid> roomIds;
        if (roomId.HasValue)
        {
            roomIds = [roomId.Value];
        }
        else
        {
            // Try to create rooms first
            var rooms = await GenerateRoomsInternalAsync(executor, logger);
            await WaitForRoomsVisibleAsync(executor, rooms.RoomIds, rooms.LastSortableUniqueId, logger);
            roomIds = rooms.RoomIds;
            if (roomIds.Count == 0)
            {
                return Results.BadRequest("No rooms available for reservations");
            }
        }

        var reservations = await GenerateReservationsInternalAsync(
            executor,
            roomIds,
            user.UserId,
            user.DisplayName,
            timeZoneOffsetMinutes,
            logger);
        await WaitForReservationsVisibleAsync(
            executor,
            reservations.ReservationIds,
            reservations.LastSortableUniqueId,
            logger);
        return Results.Ok(new
        {
            reservationsCreated = reservations.ReservationIds.Count,
            reservationIds = reservations.ReservationIds,
            errors = reservations.Errors,
            sortableUniqueId = reservations.LastSortableUniqueId
        });
    }

    private static async Task<(Guid UserId, string DisplayName)> GenerateUserInternalAsync(
        ISekibanExecutor executor,
        ILogger? logger = null)
    {
        var userId = Guid.CreateVersion7();
        var displayName = "Sample User";
        var email = $"sample.user.{userId.ToString()[..8]}@example.com";

        try
        {
            logger?.LogInformation("Registering sample user: {Email} ({UserId})", email, userId);
            await executor.ExecuteAsync(new RegisterUser
            {
                UserId = userId,
                DisplayName = displayName,
                Email = email,
                Department = "Engineering"
            });
            logger?.LogInformation("Successfully registered sample user: {Email}", email);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to register sample user (may already exist): {Email}", email);
        }

        return (userId, displayName);
    }

    private static async Task<GeneratedRoomsResult> GenerateRoomsInternalAsync(
        ISekibanExecutor executor,
        ILogger? logger = null)
    {
        var roomIds = new List<Guid>();
        string? lastSortableUniqueId = null;

        var roomDefinitions = new[]
        {
            new { Name = "Conference Room A", Capacity = 20, Location = "Building 1, Floor 2", Equipment = new List<string> { "Projector", "Whiteboard", "Video Conference" }, RequiresApproval = false },
            new { Name = "Meeting Room B", Capacity = 8, Location = "Building 1, Floor 3", Equipment = new List<string> { "TV Screen", "Whiteboard" }, RequiresApproval = false },
            new { Name = "Executive Boardroom", Capacity = 16, Location = "Building 2, Floor 5", Equipment = new List<string> { "Projector", "Video Conference", "Sound System", "Recording" }, RequiresApproval = true },
            new { Name = "Huddle Space 1", Capacity = 4, Location = "Building 1, Floor 1", Equipment = new List<string> { "TV Screen" }, RequiresApproval = false },
            new { Name = "Training Room", Capacity = 30, Location = "Building 3, Floor 1", Equipment = new List<string> { "Projector", "Multiple Screens", "Recording", "Microphones" }, RequiresApproval = true },
            new { Name = "Small Meeting Room C", Capacity = 6, Location = "Building 1, Floor 2", Equipment = new List<string> { "Whiteboard" }, RequiresApproval = false },
        };

        foreach (var roomDef in roomDefinitions)
        {
            try
            {
                var roomId = Guid.CreateVersion7();
                var command = new CreateRoom
                {
                    RoomId = roomId,
                    Name = roomDef.Name,
                    Capacity = roomDef.Capacity,
                    Location = roomDef.Location,
                    Equipment = roomDef.Equipment,
                    RequiresApproval = roomDef.RequiresApproval
                };

                logger?.LogDebug("Creating room: {RoomName} ({RoomId})", roomDef.Name, roomId);
                var result = await executor.ExecuteAsync(command);
                logger?.LogInformation("Created room: {RoomName} ({RoomId})", roomDef.Name, roomId);
                roomIds.Add(roomId);
                lastSortableUniqueId = result.SortableUniqueId ?? lastSortableUniqueId;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to create room: {RoomName}", roomDef.Name);
            }
        }

        return new GeneratedRoomsResult(roomIds, lastSortableUniqueId);
    }

    private static async Task<GeneratedReservationsResult> GenerateReservationsInternalAsync(
        ISekibanExecutor executor,
        List<Guid> roomIds,
        Guid organizerId,
        string organizerName,
        int? timeZoneOffsetMinutes,
        ILogger? logger = null)
    {
        var reservationIds = new List<Guid>();
        var errors = new List<string>();
        string? lastSortableUniqueId = null;

        var (baseDateLocal, offset) = ResolveLocalBaseDate(timeZoneOffsetMinutes);
        var baseDate = baseDateLocal.AddDays(1);

        var reservationDefinitions = new[]
        {
            new { RoomIndex = 0, DaysOffset = 0, StartHour = 9, EndHour = 10, Purpose = "Team Standup", Confirm = true },
            new { RoomIndex = 0, DaysOffset = 0, StartHour = 14, EndHour = 16, Purpose = "Sprint Planning", Confirm = true },
            new { RoomIndex = 1, DaysOffset = 1, StartHour = 10, EndHour = 11, Purpose = "1:1 Meeting", Confirm = false },
            new { RoomIndex = 2, DaysOffset = 1, StartHour = 13, EndHour = 15, Purpose = "Board Meeting", Confirm = true },
            new { RoomIndex = 4, DaysOffset = 3, StartHour = 9, EndHour = 17, Purpose = "All-hands Training", Confirm = true },
        };

        var workflow = new QuickReservationWorkflow(executor);

        foreach (var resDef in reservationDefinitions)
        {
            try
            {
                // Skip if room index exceeds available rooms
                if (resDef.RoomIndex >= roomIds.Count)
                {
                    logger?.LogWarning("Skipping reservation '{Purpose}': room index {RoomIndex} exceeds available rooms ({RoomCount})",
                        resDef.Purpose, resDef.RoomIndex, roomIds.Count);
                    continue;
                }

                var roomId = roomIds[resDef.RoomIndex];
                var startTime = new DateTimeOffset(
                    baseDate.AddDays(resDef.DaysOffset).AddHours(resDef.StartHour),
                    offset).UtcDateTime;
                var endTime = new DateTimeOffset(
                    baseDate.AddDays(resDef.DaysOffset).AddHours(resDef.EndHour),
                    offset).UtcDateTime;

                logger?.LogDebug("Creating reservation '{Purpose}' for room {RoomId} from {StartTime} to {EndTime}",
                    resDef.Purpose, roomId, startTime, endTime);

                var result = await workflow.ExecuteAsync(
                    roomId,
                    organizerId,
                    organizerName,
                    startTime,
                    endTime,
                    resDef.Purpose,
                    null);

                logger?.LogInformation("Created reservation '{Purpose}' with ID {ReservationId}", resDef.Purpose, result.ReservationId);
                reservationIds.Add(result.ReservationId);
                lastSortableUniqueId = result.SortableUniqueId;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to create reservation '{resDef.Purpose}': {ex.Message}";
                errors.Add(errorMsg);
                logger?.LogError(ex, "Failed to create reservation '{Purpose}'", resDef.Purpose);
            }
        }

        if (errors.Count > 0)
        {
            logger?.LogWarning("Reservation generation completed with {ErrorCount} errors: {Errors}",
                errors.Count, string.Join("; ", errors));
        }

        return new GeneratedReservationsResult(reservationIds, errors, lastSortableUniqueId);
    }

    private static async Task WaitForRoomsVisibleAsync(
        ISekibanExecutor executor,
        IReadOnlyCollection<Guid> roomIds,
        string? waitForSortableUniqueId,
        ILogger? logger = null)
    {
        logger?.LogDebug(
            "Skipping room visibility wait for sample test data generation. RoomCount={RoomCount}, LastSortableUniqueId={WaitForSortableUniqueId}",
            roomIds.Count,
            waitForSortableUniqueId);
        await Task.CompletedTask;
    }

    private static async Task WaitForReservationsVisibleAsync(
        ISekibanExecutor executor,
        IReadOnlyCollection<Guid> reservationIds,
        string? waitForSortableUniqueId,
        ILogger? logger = null)
    {
        logger?.LogDebug(
            "Skipping reservation visibility wait for sample test data generation. ReservationCount={ReservationCount}, LastSortableUniqueId={WaitForSortableUniqueId}",
            reservationIds.Count,
            waitForSortableUniqueId);
        await Task.CompletedTask;
    }

    private static (DateTime baseDateLocal, TimeSpan offset) ResolveLocalBaseDate(int? timeZoneOffsetMinutes)
    {
        var offset = timeZoneOffsetMinutes.HasValue
            ? TimeSpan.FromMinutes(-timeZoneOffsetMinutes.Value)
            : DateTimeOffset.Now.Offset;
        var nowLocal = DateTimeOffset.UtcNow.ToOffset(offset);
        return (nowLocal.Date, offset);
    }
}

public record TestDataGenerationResult
{
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public int RoomsCreated { get; set; }
    public List<Guid> RoomIds { get; set; } = [];
    public int ReservationsCreated { get; set; }
    public List<Guid> ReservationIds { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public string? SortableUniqueId { get; set; }
}

public record GeneratedRoomsResult(List<Guid> RoomIds, string? LastSortableUniqueId);

public record GeneratedReservationsResult(List<Guid> ReservationIds, List<string> Errors, string? LastSortableUniqueId);
