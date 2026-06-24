using Sekiban.Dcb.Tags;

namespace PublicContainerCsDecider.Domain.Weather;

// Tag — the consistency/identity boundary for a single forecast.
public record WeatherForecastTag(string ForecastId) : IStringTagGroup<WeatherForecastTag>
{
    public static string TagGroupName => "weather";

    public static WeatherForecastTag FromContent(string content) => new(content);

    public string GetId() => ForecastId;

    public bool IsConsistencyTag() => false;
}
