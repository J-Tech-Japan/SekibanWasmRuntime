using System.Text.Json;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.WasmRuntime;

namespace Sekiban.Dcb.WasmRuntime.Host;

public static class ManifestDomainTypes
{
    public static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };
        options.Converters.Add(new DynamicJsonEventPayloadJsonConverter());
        return options;
    }

    public static DcbDomainTypes Create(
        SekibanRuntimeManifest manifest,
        JsonSerializerOptions jsonOptions) =>
        new(
            eventTypes: new DynamicJsonEventTypes(manifest.EventTypes, jsonOptions),
            tagTypes: new AotTagTypes(),
            tagProjectorTypes: new ManifestTagProjectorTypes(manifest),
            tagStatePayloadTypes: new AotTagStatePayloadTypes(),
            multiProjectorTypes: CreateMultiProjectorTypes(manifest),
            queryTypes: new AotQueryTypes(),
            jsonSerializerOptions: jsonOptions);

    private static ManifestMultiProjectorTypes CreateMultiProjectorTypes(SekibanRuntimeManifest manifest)
    {
        var multiProjectorNames = manifest.QueryProjectors.Values
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var projectors = manifest.Projectors
            .Where(projector => multiProjectorNames.Contains(projector.ProjectorName))
            .Select(projector => (projector.ProjectorName, projector.ProjectorVersion));

        return new ManifestMultiProjectorTypes(projectors);
    }
}
