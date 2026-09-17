using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
///     Manifest-backed multi-projector registry for the generic WASM runtime host.
///     Guest WASM modules own projection semantics; the host only needs version lookup
///     and opaque JSON payload (de)serialization for DCB 10.22 catch-up safe promotion.
/// </summary>
public sealed class ManifestMultiProjectorTypes : ICoreMultiProjectorTypes
{
    private readonly Dictionary<string, string> _projectorVersions;

    public ManifestMultiProjectorTypes(IEnumerable<(string ProjectorName, string ProjectorVersion)> projectors)
    {
        _projectorVersions = projectors
            .GroupBy(entry => entry.ProjectorName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().ProjectorVersion,
                StringComparer.Ordinal);
    }

    public ResultBox<IMultiProjectionPayload> Project(
        string multiProjectorName,
        IMultiProjectionPayload payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold) =>
        _projectorVersions.ContainsKey(multiProjectorName)
            ? ResultBox.FromValue(payload)
            : ResultBox.Error<IMultiProjectionPayload>(
                new Exception($"Projector not found: {multiProjectorName}"));

    public ResultBox<string> GetProjectorVersion(string multiProjectorName) =>
        _projectorVersions.TryGetValue(multiProjectorName, out var version)
            ? ResultBox.FromValue(version)
            : ResultBox.Error<string>(new Exception($"Projector not found: {multiProjectorName}"));

    public IReadOnlyList<string> GetAllProjectorNames() => _projectorVersions.Keys.ToList();

    public ResultBox<Func<IMultiProjectionPayload>> GetInitialPayloadGenerator(string multiProjectorName) =>
        _projectorVersions.ContainsKey(multiProjectorName)
            ? ResultBox.FromValue<Func<IMultiProjectionPayload>>(
                () => new WasmProjectionPayload(multiProjectorName, "{}"))
            : ResultBox.Error<Func<IMultiProjectionPayload>>(
                new Exception($"Projector not found: {multiProjectorName}"));

    public ResultBox<Type> GetProjectorType(string multiProjectorName) =>
        _projectorVersions.ContainsKey(multiProjectorName)
            ? ResultBox.FromValue(typeof(WasmProjectionPayload))
            : ResultBox.Error<Type>(new Exception($"Projector not found: {multiProjectorName}"));

    public ResultBox<IMultiProjectionPayload> GenerateInitialPayload(string multiProjectorName) =>
        _projectorVersions.ContainsKey(multiProjectorName)
            ? ResultBox.FromValue<IMultiProjectionPayload>(
                new WasmProjectionPayload(multiProjectorName, "{}"))
            : ResultBox.Error<IMultiProjectionPayload>(
                new Exception($"Projector not found: {multiProjectorName}"));

    public ResultBox<IMultiProjectionPayload> Deserialize(
        byte[] data,
        string multiProjectorName,
        JsonSerializerOptions jsonOptions)
    {
        if (!_projectorVersions.ContainsKey(multiProjectorName))
        {
            return ResultBox.Error<IMultiProjectionPayload>(
                new Exception($"Projector not found: {multiProjectorName}"));
        }

        try
        {
            return ResultBox.FromValue<IMultiProjectionPayload>(
                new WasmProjectionPayload(multiProjectorName, DecodeGuestStateJson(data)));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectionPayload>(ex);
        }
    }

    public ResultBox<SerializationResult> Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload)
    {
        if (!_projectorVersions.ContainsKey(projectorName))
        {
            return ResultBox.Error<SerializationResult>(new Exception($"Projector not found: {projectorName}"));
        }

        try
        {
            byte[] jsonBytes = payload is WasmProjectionPayload wasmPayload
                ? Encoding.UTF8.GetBytes(wasmPayload.StateJson)
                : Encoding.UTF8.GetBytes("{}");
            byte[] compressed = GzipCompression.Compress(jsonBytes);
            return ResultBox.FromValue(
                new SerializationResult(compressed, jsonBytes.LongLength, compressed.LongLength));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializationResult>(ex);
        }
    }

    public ResultBox<SerializationSizeInfo> SerializeToStream(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload,
        Stream destination)
    {
        var serializeResult = Serialize(projectorName, domainTypes, safeWindowThreshold, payload);
        if (!serializeResult.IsSuccess)
        {
            return ResultBox.Error<SerializationSizeInfo>(serializeResult.GetException());
        }

        var value = serializeResult.GetValue();
        if (value.Data is { Length: > 0 })
        {
            destination.Write(value.Data, 0, value.Data.Length);
        }

        return ResultBox.FromValue(
            new SerializationSizeInfo(value.OriginalSizeBytes, value.CompressedSizeBytes));
    }

    public ResultBox<IMultiProjectionPayload> Deserialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        byte[] data)
    {
        if (!_projectorVersions.ContainsKey(projectorName))
        {
            return ResultBox.Error<IMultiProjectionPayload>(new Exception($"Projector not found: {projectorName}"));
        }

        try
        {
            return ResultBox.FromValue<IMultiProjectionPayload>(
                new WasmProjectionPayload(projectorName, DecodeGuestStateJson(data)));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectionPayload>(ex);
        }
    }

    public ResultBox<IMultiProjectionPayload> DeserializeJson(
        string projectorName,
        string json,
        DcbDomainTypes domainTypes) =>
        Deserialize(
            projectorName,
            domainTypes,
            SortableUniqueId.MinValue.Value,
            Encoding.UTF8.GetBytes(json));

    public ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        ResultBox.Error<bool>(
            new NotSupportedException(
                "Manifest-backed WASM multi projectors do not support custom serialization registration."));

    private static string DecodeGuestStateJson(byte[] data)
    {
        byte[] jsonBytes = data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b
            ? GzipCompression.Decompress(data)
            : data;
        return Encoding.UTF8.GetString(jsonBytes);
    }
}
