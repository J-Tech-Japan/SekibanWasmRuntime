using System.Text.Json.Serialization;
using PublicContainerCsDecider.Domain.Weather;

namespace PublicContainerCsDecider.Domain;

// AOT-friendly JSON context. The WASM guest is NativeAOT-compiled, so all
// serialized domain types must be source-generated here.
[JsonSerializable(typeof(WeatherForecastCreated))]
[JsonSerializable(typeof(WeatherForecastLocationUpdated))]
[JsonSerializable(typeof(WeatherForecastDeleted))]
[JsonSerializable(typeof(CreateWeatherForecast))]
[JsonSerializable(typeof(WeatherForecastState))]
[JsonSerializable(typeof(WeatherForecastItem))]
[JsonSerializable(typeof(WeatherForecastMultiProjection))]
[JsonSerializable(typeof(GetWeatherForecastListQuery))]
[JsonSerializable(typeof(Dictionary<string, WeatherForecastItem>))]
[JsonSerializable(typeof(List<WeatherForecastItem>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public partial class DomainJsonContext : JsonSerializerContext
{
}
