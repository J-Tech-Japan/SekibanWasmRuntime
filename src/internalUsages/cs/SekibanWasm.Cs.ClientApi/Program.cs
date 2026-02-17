using System.Text.Json;
using SekibanWasm.Cs.ClientApi;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Remote;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var jsonOptions = DomainJsonContext.Default.Options;
builder.Services.AddSingleton(jsonOptions);

builder.Services.AddHttpClient("wasmserver", client =>
{
    client.BaseAddress = new Uri("https+http://wasmserver");
});

builder.Services.AddTransient<HttpSerializedDcbClient>(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("wasmserver");
    var options = new SerializedDcbClientOptions { BaseUrl = "https+http://wasmserver" };
    return new HttpSerializedDcbClient(httpClient, options, sp.GetRequiredService<JsonSerializerOptions>());
});

builder.Services.AddTransient<ISerializedDcbClient>(sp =>
    sp.GetRequiredService<HttpSerializedDcbClient>());

builder.Services.AddTransient<ClientApiCommandFlow>(sp =>
    new ClientApiCommandFlow(
        sp.GetRequiredService<ISerializedDcbClient>(),
        sp.GetRequiredService<JsonSerializerOptions>()));

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

app.MapGet("/api/weatherforecast", () =>
{
    return Results.Ok(new { message = "WeatherForecast API is running" });
});

app.MapPost("/api/weatherforecast", async (
    HttpContext http,
    CreateWeatherForecast command,
    CancellationToken ct) =>
{
    var flow = http.RequestServices.GetRequiredService<ClientApiCommandFlow>();
    return await flow.ExecuteAndCommit("CreateWeatherForecast", command, ct);
});

app.MapPost("/api/weatherforecast/delete", async (
    HttpContext http,
    DeleteWeatherForecastRequest request,
    CancellationToken ct) =>
{
    var flow = http.RequestServices.GetRequiredService<ClientApiCommandFlow>();
    var command = new DeleteWeatherForecast(request.ForecastId);
    return await flow.ExecuteAndCommit("DeleteWeatherForecast", command, ct);
});

app.MapPost("/api/weatherforecast/update-location", async (
    HttpContext http,
    UpdateLocationRequest request,
    CancellationToken ct) =>
{
    var flow = http.RequestServices.GetRequiredService<ClientApiCommandFlow>();
    var command = new UpdateWeatherForecastLocation(request.ForecastId, request.NewLocation);
    return await flow.ExecuteAndCommit("UpdateWeatherForecastLocation", command, ct);
});

app.Run();

public record DeleteWeatherForecastRequest(string ForecastId);
public record UpdateLocationRequest(string ForecastId, string NewLocation);
