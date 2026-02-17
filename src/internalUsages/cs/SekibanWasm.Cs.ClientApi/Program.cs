using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("wasmserver", client =>
{
    client.BaseAddress = new Uri("https+http://wasmserver");
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOpenApi();

app.MapGet("/api/weatherforecast", () =>
{
    return Results.Ok(new { message = "WeatherForecast API is running" });
});

app.MapPost("/api/weatherforecast", async (HttpContext http, JsonElement body) =>
{
    var client = http.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("wasmserver");
    var response = await client.PostAsJsonAsync("/api/weatherforecast", body);
    var result = await response.Content.ReadAsStringAsync();
    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
});

app.MapPost("/api/weatherforecast/delete", async (HttpContext http, JsonElement body) =>
{
    var client = http.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("wasmserver");
    var response = await client.PostAsJsonAsync("/api/weatherforecast/delete", body);
    var result = await response.Content.ReadAsStringAsync();
    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
});

app.MapPost("/api/weatherforecast/update-location", async (HttpContext http, JsonElement body) =>
{
    var client = http.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("wasmserver");
    var response = await client.PostAsJsonAsync("/api/weatherforecast/update-location", body);
    var result = await response.Content.ReadAsStringAsync();
    return Results.Content(result, "application/json", statusCode: (int)response.StatusCode);
});

app.Run();
