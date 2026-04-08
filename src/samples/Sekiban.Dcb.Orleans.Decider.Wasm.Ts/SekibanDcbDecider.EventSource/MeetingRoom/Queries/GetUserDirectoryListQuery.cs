using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.EventSource.MeetingRoom.Projections;
using Dcb.EventSource.MeetingRoom.User;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.MeetingRoom.Queries;

[GenerateSerializer]
public record UserDirectoryListItem(
    [property: Id(0)] Guid UserId,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Email,
    [property: Id(3)] string? Department,
    [property: Id(4)] bool IsActive,
    [property: Id(5)] DateTime RegisteredAt,
    [property: Id(6)] int MonthlyReservationLimit,
    [property: Id(7)] List<string> ExternalProviders,
    [property: Id(8)] List<string> Roles)
{
    /// <summary>
    ///     Creates a new instance with roles
    /// </summary>
    public UserDirectoryListItem WithRoles(List<string> roles) =>
        this with { Roles = roles };
}

[GenerateSerializer]
public record GetUserDirectoryListQuery :
    IMultiProjectionListQuery<UserDirectoryListProjection, GetUserDirectoryListQuery, UserDirectoryListItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    [Id(0)]
    public int? PageNumber { get; init; }

    [Id(1)]
    public int? PageSize { get; init; }

    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }

    [Id(3)]
    public bool ActiveOnly { get; init; } = false;

    public static IEnumerable<UserDirectoryListItem> HandleFilter(
        UserDirectoryListProjection projector,
        GetUserDirectoryListQuery query,
        IQueryContext context)
    {
        var states = query.ActiveOnly
            ? projector.GetActiveUsers().Cast<UserDirectoryState>()
            : projector.GetAllUsers().Cast<UserDirectoryState>();

        return states.Select(s => s switch
        {
            UserDirectoryState.UserDirectoryActive active => new UserDirectoryListItem(
                active.UserId,
                active.DisplayName,
                active.Email,
                active.Department,
                true,
                active.RegisteredAt,
                active.MonthlyReservationLimit,
                active.ExternalIdentities.Select(e => e.Provider).ToList(),
                []),  // Roles will be populated by endpoint
            UserDirectoryState.UserDirectoryDeactivated deactivated => new UserDirectoryListItem(
                deactivated.UserId,
                deactivated.DisplayName,
                deactivated.Email,
                deactivated.Department,
                false,
                deactivated.RegisteredAt,
                deactivated.MonthlyReservationLimit,
                deactivated.ExternalIdentities.Select(e => e.Provider).ToList(),
                []),  // Roles will be populated by endpoint
            _ => null
        })
        .Where(x => x != null)
        .Cast<UserDirectoryListItem>();
    }

    public static IEnumerable<UserDirectoryListItem> HandleSort(
        IEnumerable<UserDirectoryListItem> filteredList,
        GetUserDirectoryListQuery query,
        IQueryContext context) =>
        filteredList.OrderBy(s => s.DisplayName, StringComparer.Ordinal);
}
