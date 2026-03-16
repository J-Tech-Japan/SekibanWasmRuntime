using Sekiban.Dcb.Primitives;

namespace Sekiban.Dcb.WasmRuntime.Host;

public static class InstanceEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/v1/instances", (
            CreateInstanceRequest request,
            IPrimitiveProjectionHost host,
            ProjectionInstanceStore store) =>
            CreateInstanceResult(request, host, store));

        app.MapPost("/v1/instances/{id}/events", (
            string id,
            ApplyEventsRequest request,
            ProjectionInstanceStore store) =>
        {
            if (!store.TryGet(id, out var instance))
            {
                return Results.NotFound(new { error = $"Instance '{id}' not found." });
            }

            foreach (var ev in request.Events)
            {
                instance!.ApplyEvent(ev.EventType, ev.PayloadJson, ev.Tags, ev.SortableUniqueId);
            }

            return Results.Ok();
        });

        app.MapPost("/v1/instances/{id}/query", (
            string id,
            QueryRequest request,
            ProjectionInstanceStore store) =>
        {
            if (!store.TryGet(id, out var instance))
            {
                return Results.NotFound(new { error = $"Instance '{id}' not found." });
            }

            var resultJson = instance!.ExecuteQuery(request.QueryType, request.QueryParamsJson);
            return Results.Ok(new { resultJson });
        });

        app.MapPost("/v1/instances/{id}/list-query", (
            string id,
            QueryRequest request,
            ProjectionInstanceStore store) =>
        {
            if (!store.TryGet(id, out var instance))
            {
                return Results.NotFound(new { error = $"Instance '{id}' not found." });
            }

            var resultJson = instance!.ExecuteListQuery(request.QueryType, request.QueryParamsJson);
            return Results.Ok(new { resultJson });
        });

        app.MapGet("/v1/instances/{id}/snapshot", (string id, ProjectionInstanceStore store) =>
        {
            if (!store.TryGet(id, out var instance))
            {
                return Results.NotFound(new { error = $"Instance '{id}' not found." });
            }

            var stateJson = instance!.SerializeState();
            return Results.Ok(new { stateJson });
        });

        app.MapPut("/v1/instances/{id}/snapshot", (
            string id,
            RestoreStateRequest request,
            ProjectionInstanceStore store) =>
        {
            if (!store.TryGet(id, out var instance))
            {
                return Results.NotFound(new { error = $"Instance '{id}' not found." });
            }

            instance!.RestoreState(request.StateJson);
            return Results.Ok();
        });

        app.MapDelete("/v1/instances/{id}", (string id, ProjectionInstanceStore store) =>
        {
            if (store.Remove(id))
            {
                return Results.Ok();
            }

            return Results.NotFound(new { error = $"Instance '{id}' not found." });
        });
    }

    internal static IResult CreateInstanceResult(
        CreateInstanceRequest request,
        IPrimitiveProjectionHost host,
        ProjectionInstanceStore store)
    {
        try
        {
            var instance = host.CreateInstance(request.ProjectorName);
            var instanceId = store.Add(instance);
            return Results.Ok(new { instanceId });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public record CreateInstanceRequest(string ProjectorName);

public record EventData(string EventType, string PayloadJson, List<string> Tags, string? SortableUniqueId);

public record ApplyEventsRequest(List<EventData> Events);

public record QueryRequest(string QueryType, string QueryParamsJson);

public record RestoreStateRequest(string StateJson);
