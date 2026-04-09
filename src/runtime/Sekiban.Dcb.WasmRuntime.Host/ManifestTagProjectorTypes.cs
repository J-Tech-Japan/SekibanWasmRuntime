using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime.Host;

internal sealed class ManifestTagProjectorTypes : ITagProjectorTypes
{
    private readonly Dictionary<string, string> _projectorVersions;

    public ManifestTagProjectorTypes(SekibanRuntimeManifest manifest)
    {
        _projectorVersions = manifest.Projectors
            .GroupBy(projector => projector.ProjectorName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().ProjectorVersion,
                StringComparer.Ordinal);
    }

    public ResultBox<Func<ITagStatePayload, Event, ITagStatePayload>> GetProjectorFunction(string tagProjectorName) =>
        ResultBox.Error<Func<ITagStatePayload, Event, ITagStatePayload>>(
            new Exception($"Manifest-backed tag projector '{tagProjectorName}' does not expose a native projector function."));

    public ResultBox<string> GetProjectorVersion(string tagProjectorName)
    {
        if (_projectorVersions.TryGetValue(tagProjectorName, out var version))
        {
            return ResultBox.FromValue(version);
        }

        return ResultBox.Error<string>(new Exception($"Tag projector '{tagProjectorName}' not found"));
    }

    public IReadOnlyList<string> GetAllProjectorNames() => _projectorVersions.Keys.ToList();

    public string? TryGetProjectorForTagGroup(string tagGroupName)
    {
        if (string.IsNullOrWhiteSpace(tagGroupName))
        {
            return null;
        }

        var conventionName = $"{tagGroupName}Projector";
        if (_projectorVersions.ContainsKey(conventionName))
        {
            return conventionName;
        }

        return _projectorVersions.Keys
            .FirstOrDefault(key => key.StartsWith(tagGroupName, StringComparison.OrdinalIgnoreCase));
    }
}
