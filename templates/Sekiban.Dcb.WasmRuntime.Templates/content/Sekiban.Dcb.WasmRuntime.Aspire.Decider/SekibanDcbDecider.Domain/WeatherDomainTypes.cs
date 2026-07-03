using SekibanDcbDecider.Domain.Weather;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;

namespace SekibanDcbDecider.Domain;

// AOT domain-types registration for the WASM guest. Mirrors the manifest the
// runtime container loads (weather tag group, WeatherForecastProjector,
// WeatherForecastMultiProjection, GetWeatherForecastListQuery).
public static class WeatherDomainTypes
{
    public static DcbDomainTypes CreateWasmDomainTypes()
    {
        var builder = new AotDomainTypesBuilder(DomainJsonContext.Default.Options);

        builder.EventTypes.Register(
            nameof(WeatherForecastCreated),
            DomainJsonContext.Default.WeatherForecastCreated);
        builder.EventTypes.Register(
            nameof(WeatherForecastLocationUpdated),
            DomainJsonContext.Default.WeatherForecastLocationUpdated);
        builder.EventTypes.Register(
            nameof(WeatherForecastDeleted),
            DomainJsonContext.Default.WeatherForecastDeleted);

        builder.TagProjectorTypes.RegisterProjector<WeatherForecastProjector>();
        builder.TagStatePayloadTypes.Register(
            nameof(WeatherForecastState),
            DomainJsonContext.Default.WeatherForecastState);
        builder.TagTypes.RegisterTagGroupType<WeatherForecastTag>();

        return builder.Build();
    }
}
