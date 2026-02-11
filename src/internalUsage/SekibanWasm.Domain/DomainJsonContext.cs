using System.Text.Json.Serialization;
using SekibanWasm.Domain.Weather;

namespace SekibanWasm.Domain;

[JsonSerializable(typeof(WeatherForecastCreated))]
[JsonSerializable(typeof(WeatherForecastLocationUpdated))]
[JsonSerializable(typeof(WeatherForecastDeleted))]
[JsonSerializable(typeof(WeatherForecastState))]
[JsonSerializable(typeof(WeatherForecastItem))]
[JsonSerializable(typeof(WeatherForecastMultiProjection))]
[JsonSerializable(typeof(Dictionary<string, WeatherForecastItem>))]
[JsonSerializable(typeof(List<WeatherForecastItem>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
public partial class DomainJsonContext : JsonSerializerContext
{
}
