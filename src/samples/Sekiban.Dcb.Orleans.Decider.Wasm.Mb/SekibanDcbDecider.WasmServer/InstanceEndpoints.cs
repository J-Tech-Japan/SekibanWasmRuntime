using System.Collections.Concurrent;
using Sekiban.Dcb.Primitives;

public static class InstanceEndpoints
{
    private static readonly ConcurrentDictionary<string, IPrimitiveProjectionInstance> Instances = new();

    public static void Map(WebApplication app)
    {
        app.MapPost("/v1/instances", (HttpContext http, CreateInstanceRequest request) =>
        {
            var host = http.RequestServices.GetRequiredService<IPrimitiveProjectionHost>();
            var instance = host.CreateInstance(request.ProjectorName);
            var instanceId = Guid.NewGuid().ToString();
            Instances[instanceId] = instance;
            return Results.Ok(new { instanceId });
        });

        app.MapPost("/v1/instances/{id}/events", (string id, ApplyEventsRequest request) =>
        {
            var instance = GetInstance(id);
            foreach (var ev in request.Events)
            {
                instance.ApplyEvent(ev.EventType, ev.PayloadJson, ev.Tags, ev.SortableUniqueId);
            }
            return Results.Ok();
        });

        app.MapPost("/v1/instances/{id}/query", (string id, QueryRequest request) =>
        {
            var instance = GetInstance(id);
            var resultJson = instance.ExecuteQuery(request.QueryType, request.QueryParamsJson);
            return Results.Ok(new { resultJson });
        });

        app.MapPost("/v1/instances/{id}/list-query", (string id, QueryRequest request) =>
        {
            var instance = GetInstance(id);
            var resultJson = instance.ExecuteListQuery(request.QueryType, request.QueryParamsJson);
            return Results.Ok(new { resultJson });
        });

        app.MapGet("/v1/instances/{id}/snapshot", (string id) =>
        {
            var instance = GetInstance(id);
            var stateJson = instance.SerializeState();
            return Results.Ok(new { stateJson });
        });

        app.MapPut("/v1/instances/{id}/snapshot", (string id, RestoreStateRequest request) =>
        {
            var instance = GetInstance(id);
            instance.RestoreState(request.StateJson);
            return Results.Ok();
        });

        app.MapDelete("/v1/instances/{id}", (string id) =>
        {
            if (Instances.TryRemove(id, out var instance))
            {
                instance.Dispose();
            }
            return Results.Ok();
        });
    }

    private static IPrimitiveProjectionInstance GetInstance(string id)
    {
        if (!Instances.TryGetValue(id, out var instance))
        {
            throw new KeyNotFoundException($"Instance '{id}' not found");
        }
        return instance;
    }
}

public record CreateInstanceRequest(string ProjectorName);

public record EventData(string EventType, string PayloadJson, List<string> Tags, string? SortableUniqueId);

public record ApplyEventsRequest(List<EventData> Events);

public record QueryRequest(string QueryType, string QueryParamsJson);

public record RestoreStateRequest(string StateJson);
