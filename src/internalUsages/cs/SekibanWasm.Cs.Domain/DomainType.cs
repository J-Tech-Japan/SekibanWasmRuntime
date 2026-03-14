using Sekiban.Dcb;
using Sekiban.Dcb.Domains;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.Domain;

public static class DomainType
{
    private static readonly Lazy<DcbDomainTypes> RuntimeDomainTypes = new(CreateRuntimeDomainTypes);
    private static readonly Lazy<DcbDomainTypes> WasmDomainTypes = new(CreateWasmDomainTypes);

    public static DcbDomainTypes GetDomainTypes() => RuntimeDomainTypes.Value;

    public static DcbDomainTypes GetWasmDomainTypes() => WasmDomainTypes.Value;

    private static DcbDomainTypes CreateRuntimeDomainTypes() =>
        DcbDomainTypesExtensions.Simple(types =>
        {
            RegisterCommonRuntimeTypes(types);
            types.MultiProjectorTypes.RegisterProjector<WeatherForecastMultiProjection>();
            types.QueryTypes.RegisterListQuery<GetWeatherForecastListQuery>();
        });

    private static DcbDomainTypes CreateWasmDomainTypes()
    {
        var builder = new AotDomainTypesBuilder(DomainJsonContext.Default.Options);
        RegisterCommonAotTypes(builder);
        return builder.Build();
    }

    private static void RegisterCommonRuntimeTypes(DcbDomainTypesExtensions.Builder types)
    {
        types.EventTypes.RegisterEventType<WeatherForecastCreated>();
        types.EventTypes.RegisterEventType<WeatherForecastLocationUpdated>();
        types.EventTypes.RegisterEventType<WeatherForecastDeleted>();

        types.TagProjectorTypes.RegisterProjector<WeatherForecastProjector>();
        types.TagStatePayloadTypes.RegisterPayloadType<WeatherForecastState>();
        types.TagTypes.RegisterTagGroupType<WeatherForecastTag>();
    }

    private static void RegisterCommonAotTypes(AotDomainTypesBuilder builder)
    {
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
    }
}
