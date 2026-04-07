using Dcb.MeetingRoomModels.States.UserMonthlyReservation;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.States;

public class UserMonthlyReservationStateTests
{
    [Fact]
    public void RegisterRequest_Should_Initialize_Identity_And_Increment_Total()
    {
        var userId = Guid.NewGuid();
        var month = new DateOnly(2026, 4, 1);

        var state = UserMonthlyReservationState.Empty.RegisterRequest(userId, month);

        Assert.Equal(userId, state.UserId);
        Assert.Equal(month, state.Month);
        Assert.Equal(1, state.TotalRequests);
        Assert.Equal(0, state.RejectedRequests);
        Assert.Equal(1, state.ActiveRequestCount);
    }

    [Fact]
    public void RegisterRejection_Before_Identity_Initialization_Should_Return_Same_State()
    {
        var empty = UserMonthlyReservationState.Empty;
        var state = empty.RegisterRejection();

        Assert.Equal(empty, state);
        Assert.Equal(Guid.Empty, state.UserId);
        Assert.Equal(default, state.Month);
        Assert.Equal(0, state.TotalRequests);
        Assert.Equal(0, state.RejectedRequests);
    }

    [Fact]
    public void RegisterRejection_Should_Clamp_To_TotalRequests()
    {
        var userId = Guid.NewGuid();
        var month = new DateOnly(2026, 4, 1);
        var state = UserMonthlyReservationState.Empty.RegisterRequest(userId, month);

        state = state.RegisterRejection();
        state = state.RegisterRejection();

        Assert.Equal(1, state.TotalRequests);
        Assert.Equal(1, state.RejectedRequests);
        Assert.Equal(0, state.ActiveRequestCount);
    }

    [Fact]
    public void RegisterRequest_Should_Preserve_Existing_Identity()
    {
        var userId = Guid.NewGuid();
        var month = new DateOnly(2026, 4, 1);
        var state = UserMonthlyReservationState.Empty.RegisterRequest(userId, month);

        var updated = state.RegisterRequest(Guid.NewGuid(), new DateOnly(2026, 5, 1));

        Assert.Equal(userId, updated.UserId);
        Assert.Equal(month, updated.Month);
        Assert.Equal(2, updated.TotalRequests);
    }
}
