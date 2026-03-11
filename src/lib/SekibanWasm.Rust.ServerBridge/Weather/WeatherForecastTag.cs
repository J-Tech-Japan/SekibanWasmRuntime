using Sekiban.Dcb.Tags;

namespace SekibanWasm.Rust.Domain.Weather;

public record WeatherForecastTag(string ForecastId) : IStringTagGroup<WeatherForecastTag>
{
    public static string TagGroupName => "weather";

    public static WeatherForecastTag FromContent(string content) => new(content);

    public string GetId() => ForecastId;

    public bool IsConsistencyTag() => false;
}
