using Sekiban.Dcb;
using Sekiban.Dcb.Domains;
using SekibanWasm.Domain;
using SekibanWasm.Domain.Weather;

namespace SekibanWasm.Wasm;

public static class WasmDomainTypes
{
    private static DcbDomainTypes? _instance;

    public static DcbDomainTypes Create()
    {
        if (_instance != null) return _instance;

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

        builder.TagStatePayloadTypes.Register(
            nameof(WeatherForecastState),
            DomainJsonContext.Default.WeatherForecastState);

        builder.TagTypes.RegisterTagGroupType<WeatherForecastTag>();
        builder.TagProjectorTypes.RegisterProjector<WeatherForecastProjector>();

        _instance = builder.Build();
        return _instance;
    }
}
