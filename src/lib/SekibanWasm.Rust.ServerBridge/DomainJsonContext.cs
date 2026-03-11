using System.Text.Json.Serialization;
using SekibanWasm.Rust.Domain.Weather;

namespace SekibanWasm.Rust.Domain;

[JsonSerializable(typeof(WeatherForecastCreated))]
[JsonSerializable(typeof(WeatherForecastLocationUpdated))]
[JsonSerializable(typeof(WeatherForecastDeleted))]
[JsonSerializable(typeof(CreateWeatherForecast))]
[JsonSerializable(typeof(UpdateWeatherForecastLocation))]
[JsonSerializable(typeof(DeleteWeatherForecast))]
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
