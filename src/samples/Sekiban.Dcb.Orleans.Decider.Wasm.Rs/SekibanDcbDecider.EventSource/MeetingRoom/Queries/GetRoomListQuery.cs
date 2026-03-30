using Dcb.MeetingRoomModels.States.Room;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.EventSource.MeetingRoom.Room;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.MeetingRoom.Queries;

[GenerateSerializer]
public record RoomListItem(
    [property: Id(0)] Guid RoomId,
    [property: Id(1)] string Name,
    [property: Id(2)] int Capacity,
    [property: Id(3)] string Location,
    [property: Id(4)] List<string> Equipment,
    [property: Id(5)] bool RequiresApproval,
    [property: Id(6)] bool IsActive);

public record GetRoomListQuery :
    IMultiProjectionListQuery<RoomListProjection, GetRoomListQuery, RoomListItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    [Id(0)]
    public int? PageNumber { get; init; }

    [Id(1)]
    public int? PageSize { get; init; }

    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }

    public static IEnumerable<RoomListItem> HandleFilter(
        RoomListProjection projector,
        GetRoomListQuery query,
        IQueryContext context)
    {
        return projector.GetAllRooms()
            .Select(s => new RoomListItem(
                s.RoomId,
                s.Name,
                s.Capacity,
                s.Location,
                s.Equipment,
                s.RequiresApproval,
                s.IsActive));
    }

    public static IEnumerable<RoomListItem> HandleSort(
        IEnumerable<RoomListItem> filteredList,
        GetRoomListQuery query,
        IQueryContext context) =>
        filteredList.OrderBy(s => s.Name, StringComparer.Ordinal);
}
