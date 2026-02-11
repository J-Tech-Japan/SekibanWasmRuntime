using System.Collections.Concurrent;
using System.Text.Json;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;

var builder = WebApplication.CreateBuilder(args);

var wasmModulePath = builder.Configuration["Wasm:DefaultModulePath"]
    ?? throw new InvalidOperationException(
        "Wasm:DefaultModulePath configuration is required. " +
        "Set the Wasm__DefaultModulePath environment variable to the absolute path of the .wasm module.");

var registry = new WasmProjectorRegistry();
registry.Register(new WasmModuleRef(
    ProjectorName: "WeatherForecastMultiProjection",
    ModulePath: wasmModulePath,
    AbiKind: "wasi-preview1",
    ModuleVersion: "1.0.0",
    ProjectorVersion: "1.0.0"));

builder.Services.AddSingleton(registry);
builder.Services.AddWasmtimeProjectionHost(opt =>
{
    opt.DefaultModulePath = wasmModulePath;
});

var app = builder.Build();

var instances = new ConcurrentDictionary<string, IPrimitiveProjectionInstance>();

app.MapPost("/v1/instances", async (HttpContext http, IPrimitiveProjectionHost host) =>
{
    using var doc = await JsonDocument.ParseAsync(http.Request.Body);
    var root = doc.RootElement;
    var projectorName = root.GetProperty("projectorName").GetString()
        ?? throw new InvalidOperationException("projectorName is required");

    var instanceId = Guid.NewGuid().ToString();
    var instance = host.CreateInstance(projectorName);
    if (!instances.TryAdd(instanceId, instance))
    {
        instance.Dispose();
        return Results.Problem("Failed to register instance", statusCode: 500);
    }
    return Results.Ok(new { instanceId });
});

app.MapPost("/v1/instances/{id}/events", async (string id, HttpContext http) =>
{
    if (!instances.TryGetValue(id, out var instance))
    {
        return Results.NotFound(new { error = $"Instance '{id}' not found" });
    }

    using var doc = await JsonDocument.ParseAsync(http.Request.Body);
    var root = doc.RootElement;
    var events = root.GetProperty("events");

    foreach (var ev in events.EnumerateArray())
    {
        var eventType = ev.GetProperty("eventType").GetString()!;
        var payloadJson = ev.GetProperty("payloadJson").GetString()!;
        var tags = ev.TryGetProperty("tags", out var tagsEl)
            ? tagsEl.EnumerateArray().Select(t => t.GetString()!).ToList()
            : new List<string>();
        var sortableUniqueId = ev.TryGetProperty("sortableUniqueId", out var suid)
            ? suid.GetString()
            : null;

        instance.ApplyEvent(eventType, payloadJson, tags, sortableUniqueId);
    }
    return Results.Ok();
});

app.MapPost("/v1/instances/{id}/query", async (string id, HttpContext http) =>
{
    if (!instances.TryGetValue(id, out var instance))
    {
        return Results.NotFound(new { error = $"Instance '{id}' not found" });
    }

    using var doc = await JsonDocument.ParseAsync(http.Request.Body);
    var root = doc.RootElement;
    var queryType = root.GetProperty("queryType").GetString()!;
    var queryParamsJson = root.GetProperty("queryParamsJson").GetString()!;

    var resultJson = instance.ExecuteQuery(queryType, queryParamsJson);
    return Results.Ok(new { resultJson });
});

app.MapPost("/v1/instances/{id}/list-query", async (string id, HttpContext http) =>
{
    if (!instances.TryGetValue(id, out var instance))
    {
        return Results.NotFound(new { error = $"Instance '{id}' not found" });
    }

    using var doc = await JsonDocument.ParseAsync(http.Request.Body);
    var root = doc.RootElement;
    var queryType = root.GetProperty("queryType").GetString()!;
    var queryParamsJson = root.GetProperty("queryParamsJson").GetString()!;

    var resultJson = instance.ExecuteListQuery(queryType, queryParamsJson);
    return Results.Ok(new { resultJson });
});

app.MapGet("/v1/instances/{id}/snapshot", (string id) =>
{
    if (!instances.TryGetValue(id, out var instance))
    {
        return Results.NotFound(new { error = $"Instance '{id}' not found" });
    }

    var stateJson = instance.SerializeState();
    return Results.Ok(new { stateJson });
});

app.MapPut("/v1/instances/{id}/snapshot", async (string id, HttpContext http) =>
{
    if (!instances.TryGetValue(id, out var instance))
    {
        return Results.NotFound(new { error = $"Instance '{id}' not found" });
    }

    using var doc = await JsonDocument.ParseAsync(http.Request.Body);
    var stateJson = doc.RootElement.GetProperty("stateJson").GetString()!;
    instance.RestoreState(stateJson);
    return Results.Ok();
});

app.MapDelete("/v1/instances/{id}", (string id) =>
{
    if (instances.TryRemove(id, out var instance))
    {
        instance.Dispose();
        return Results.Ok();
    }
    return Results.NotFound(new { error = $"Instance '{id}' not found" });
});

app.Run();
