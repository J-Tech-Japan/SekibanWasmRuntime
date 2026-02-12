using Sekiban.Dcb;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.Domain;

public static class DomainType
{
    public static DcbDomainTypes GetDomainTypes()
    {
        return DcbDomainTypesExtensions.Simple(types =>
        {
            types.EventTypes.RegisterEventType<WeatherForecastCreated>();
            types.EventTypes.RegisterEventType<WeatherForecastLocationUpdated>();
            types.EventTypes.RegisterEventType<WeatherForecastDeleted>();

            types.TagProjectorTypes.RegisterProjector<WeatherForecastProjector>();

            types.TagStatePayloadTypes.RegisterPayloadType<WeatherForecastState>();

            types.TagTypes.RegisterTagGroupType<WeatherForecastTag>();
        });
    }
}
