using System.Text.Json;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;

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
            tagProjectorTypes: new AotTagProjectorTypes(),
            tagStatePayloadTypes: new AotTagStatePayloadTypes(),
            multiProjectorTypes: new AotMultiProjectorTypes(),
            queryTypes: new AotQueryTypes(),
            jsonSerializerOptions: jsonOptions);
}
