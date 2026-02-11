using SekibanWasm.Domain;
using SekibanWasm.Domain.Weather;
using Sekiban.Dcb;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Domain types
var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);

// Sekiban DCB setup
builder.Services.AddSingleton<Sekiban.Dcb.ServiceId.IServiceIdProvider, Sekiban.Dcb.ServiceId.DefaultServiceIdProvider>();
builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
builder.Services.AddSekibanDcbPostgresWithAspire();

// Native runtime
builder.Services.AddSekibanDcbNativeRuntime();

// OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

// WeatherForecast API endpoints
app.MapPost("/api/weatherforecast", async (CreateWeatherForecast command, ISekibanExecutor executor) =>
{
    var result = await executor.ExecuteAsync(command);
    return Results.Ok(result);
});

app.MapGet("/api/weatherforecast", () =>
{
    return Results.Ok(new { message = "WeatherForecast API is running" });
});

app.Run();
