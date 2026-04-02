using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.States.Weather;
using Dcb.ImmutableModels.States.Weather.Deciders;

namespace SekibanDcbOrleans.ImmutableModels.Unit;

public class UnitTest1
{
    [Fact]
    public void WeatherForecastDeletedDecider_Evolve_Sets_IsDeleted()
    {
        var state = new WeatherForecastState(
            Guid.NewGuid(),
            "Tokyo",
            new DateOnly(2026, 1, 1),
            24,
            "Sunny");

        var evolved = state.Evolve(new WeatherForecastDeleted(state.ForecastId));

        Assert.True(evolved.IsDeleted);
    }

    [Fact]
    public void WeatherForecastDeletedDecider_Validate_Throws_For_Deleted_State()
    {
        var forecastId = Guid.NewGuid();
        var deleted = new WeatherForecastState(
            forecastId,
            "Tokyo",
            new DateOnly(2026, 1, 1),
            24,
            "Sunny",
            true);

        var ex = Assert.Throws<InvalidOperationException>(() => WeatherForecastDeletedDecider.Validate(deleted));

        Assert.Contains(forecastId.ToString(), ex!.Message, StringComparison.OrdinalIgnoreCase);
    }
}
