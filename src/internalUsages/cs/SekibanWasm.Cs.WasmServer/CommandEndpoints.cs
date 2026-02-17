using SekibanWasm.Cs.Domain.Weather;
using Sekiban.Dcb;

public static class CommandEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/weatherforecast", async (HttpContext http, CreateWeatherForecast command) =>
        {
            var executor = http.RequestServices.GetRequiredService<ISekibanExecutor>();
            var result = await executor.ExecuteAsync(command);
            return Results.Ok(result);
        });

        app.MapGet("/api/weatherforecast", () =>
        {
            return Results.Ok(new { message = "WeatherForecast API is running" });
        });

        app.MapPost("/api/weatherforecast/delete", async (HttpContext http, DeleteWeatherForecastRequest request) =>
        {
            var executor = http.RequestServices.GetRequiredService<ISekibanExecutor>();
            var command = new DeleteWeatherForecast(request.ForecastId);
            var result = await executor.ExecuteAsync(command);
            return Results.Ok(result);
        });

        app.MapPost("/api/weatherforecast/update-location", async (HttpContext http, UpdateLocationRequest request) =>
        {
            var executor = http.RequestServices.GetRequiredService<ISekibanExecutor>();
            var command = new UpdateWeatherForecastLocation(request.ForecastId, request.NewLocation);
            var result = await executor.ExecuteAsync(command);
            return Results.Ok(result);
        });
    }
}

public record DeleteWeatherForecastRequest(string ForecastId);
public record UpdateLocationRequest(string ForecastId, string NewLocation);
