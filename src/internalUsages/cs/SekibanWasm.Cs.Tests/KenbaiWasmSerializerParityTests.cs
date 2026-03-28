using System.Text.Json;
using System.Text.Json.Nodes;
using Aic.Kenbai.EventSource;
using Aic.Kenbai.EventSource.Projections.HokenNendoShosais;
using Aic.Kenbai.EventSource.Projections.Kanyushyas;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class KenbaiWasmSerializerParityTests
{
    private const string ProblematicNendoKanyuReplacedSortableUniqueId = "063906753820290312601948056607";
    private const string Kanyusha10002NendoReplaceSortableUniqueId = "063906751617519564100062927599";
    private const string Kanyusha10002RelatedKeiyakuReplaceSortableUniqueId = "063906751618119942702050950035";
    private static readonly JsonSerializerOptions SerializerOptions =
        KenbaiDcbDomainType.GetDomainTypes().JsonSerializerOptions;

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(1000)]
    public void KanyushaListProjectionReplay_ShouldMatchNative_ForLeadingEvents(int takeCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEvents(takeCount);

        AssertProjectionParity(
            rows,
            KenbaiWasmParityTestSupport.ReplayKanyushaListProjectionNative(rows),
            KenbaiWasmParityTestSupport.ReplayProjectionWasm(KanyushaListProjection.MultiProjectorName, rows));
    }

    [Theory]
    [InlineData(10, 5)]
    [InlineData(100, 50)]
    [InlineData(300, 150)]
    [InlineData(1000, 500)]
    [InlineData(132500, 120000)]
    [InlineData(135000, 120000)]
    [InlineData(137500, 120000)]
    [InlineData(130000, 120000)]
    [InlineData(140000, 120000)]
    [InlineData(145000, 120000)]
    [InlineData(147500, 120000)]
    [InlineData(149000, 120000)]
    [InlineData(150000, 120000)]
    public void KanyushaListProjectionSnapshotRestore_ShouldMatchNative_ForLeadingEvents(
        int takeCount,
        int restoreAfterCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEvents(takeCount);

        AssertProjectionParity(
            rows,
            KenbaiWasmParityTestSupport.ReplayKanyushaListProjectionNative(rows),
            KenbaiWasmParityTestSupport.ReplayProjectionWasmWithRestore(
                KanyushaListProjection.MultiProjectorName,
                rows,
                restoreAfterCount));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(1000)]
    public void HokenNendoShosaiListProjectionReplay_ShouldMatchNative_ForLeadingEvents(int takeCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEvents(takeCount);

        AssertProjectionParity(
            rows,
            KenbaiWasmParityTestSupport.ReplayHokenNendoShosaiListProjectionNative(rows),
            KenbaiWasmParityTestSupport.ReplayProjectionWasm(
                HokenNendoShosaiListProjection.MultiProjectorName,
                rows));
    }

    [Theory]
    [InlineData(100, 50)]
    [InlineData(300, 150)]
    [InlineData(1000, 500)]
    public void HokenNendoShosaiListProjectionSnapshotRestore_ShouldMatchNative_ForLeadingEvents(
        int takeCount,
        int restoreAfterCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEvents(takeCount);

        AssertProjectionParity(
            rows,
            KenbaiWasmParityTestSupport.ReplayHokenNendoShosaiListProjectionNative(rows),
            KenbaiWasmParityTestSupport.ReplayProjectionWasmWithRestore(
                HokenNendoShosaiListProjection.MultiProjectorName,
                rows,
                restoreAfterCount));
    }

    [Fact]
    public void KanyushaListProjectionRestoreThenApplyProblematicNendoReplace_ShouldMatchNativeForAffectedItem()
    {
        const int checkpointEventCount = 145235;

        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> checkpointRows =
            KenbaiWasmParityTestSupport.LoadOrderedEvents(checkpointEventCount);
        KenbaiWasmParityTestSupport.EventRow problematicRow =
            KenbaiWasmParityTestSupport.LoadEventBySortableUniqueId(ProblematicNendoKanyuReplacedSortableUniqueId);

        KanyushaListProjection nativeState =
            KenbaiWasmParityTestSupport.ReplayKanyushaListProjectionNative(checkpointRows.Concat([problematicRow]));
        string nativeJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            JsonSerializer.Serialize(nativeState, SerializerOptions));

        string checkpointSnapshot = KenbaiWasmParityTestSupport.ReplayProjectionWasm(
            KanyushaListProjection.MultiProjectorName,
            checkpointRows);

        List<string> tagStrings = JsonSerializer.Deserialize<List<string>>(problematicRow.TagsJson) ?? [];
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        int instanceId = wasm.TryCreateInstance(KanyushaListProjection.MultiProjectorName);
        Assert.True(instanceId > 0, $"Failed to create {KanyushaListProjection.MultiProjectorName}: {instanceId}");

        wasm.RestoreState(instanceId, checkpointSnapshot);
        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, problematicRow.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, KenbaiWasmParityTestSupport.CanonicalizeJson(problematicRow.TagsJson));
        int parsedTags = wasm.DebugBufferedSortableParseTags(
            instanceId,
            problematicRow.EventType,
            problematicRow.SortableUniqueId);
        Assert.Equal(tagStrings.Count, parsedTags);

        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, problematicRow.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, KenbaiWasmParityTestSupport.CanonicalizeJson(problematicRow.TagsJson));
        wasm.ApplyBufferedEventWithSortable(
            instanceId,
            problematicRow.EventType,
            problematicRow.SortableUniqueId);

        string wasmJson = KenbaiWasmParityTestSupport.ExtractCanonicalStateJsonFromRawSnapshot(
            wasm.SerializeState(instanceId));

        string nativeAffectedItemJson = ExtractKanyushaItemJson(nativeJson, "4");
        string wasmAffectedItemJson = ExtractKanyushaItemJson(wasmJson, "4");

        Assert.Equal(nativeAffectedItemJson, wasmAffectedItemJson);
    }

    [Fact]
    public void KanyushaListProjectionRestoreThenApplyKanyusha10002NendoReplace_ShouldMatchNativeForAffectedItem()
    {
        const int checkpointEventCount = 132500;

        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> checkpointRows =
            KenbaiWasmParityTestSupport.LoadOrderedEvents(checkpointEventCount);
        KenbaiWasmParityTestSupport.EventRow problematicRow =
            KenbaiWasmParityTestSupport.LoadEventBySortableUniqueId(Kanyusha10002NendoReplaceSortableUniqueId);

        KanyushaListProjection nativeState =
            KenbaiWasmParityTestSupport.ReplayKanyushaListProjectionNative(checkpointRows.Concat([problematicRow]));
        string nativeJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            JsonSerializer.Serialize(nativeState, SerializerOptions));

        string checkpointSnapshot = KenbaiWasmParityTestSupport.ReplayProjectionWasm(
            KanyushaListProjection.MultiProjectorName,
            checkpointRows);

        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        int instanceId = wasm.TryCreateInstance(KanyushaListProjection.MultiProjectorName);
        Assert.True(instanceId > 0, $"Failed to create {KanyushaListProjection.MultiProjectorName}: {instanceId}");

        wasm.RestoreState(instanceId, checkpointSnapshot);
        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, problematicRow.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, KenbaiWasmParityTestSupport.CanonicalizeJson(problematicRow.TagsJson));
        wasm.ApplyBufferedEventWithSortable(
            instanceId,
            problematicRow.EventType,
            problematicRow.SortableUniqueId);

        string wasmJson = KenbaiWasmParityTestSupport.ExtractCanonicalStateJsonFromRawSnapshot(
            wasm.SerializeState(instanceId));

        string nativeAffectedItemJson = ExtractKanyushaItemJson(nativeJson, "10002");
        string wasmAffectedItemJson = ExtractKanyushaItemJson(wasmJson, "10002");

        Assert.Equal(nativeAffectedItemJson, wasmAffectedItemJson);
    }

    [Fact]
    public void KanyushaListProjectionRestoreThenApplyKanyusha10002NendoAndKeiyakuReplace_ShouldMatchNativeForAffectedItem()
    {
        const int checkpointEventCount = 132500;

        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> checkpointRows =
            KenbaiWasmParityTestSupport.LoadOrderedEvents(checkpointEventCount);
        KenbaiWasmParityTestSupport.EventRow nendoRow =
            KenbaiWasmParityTestSupport.LoadEventBySortableUniqueId(Kanyusha10002NendoReplaceSortableUniqueId);
        KenbaiWasmParityTestSupport.EventRow keiyakuRow =
            KenbaiWasmParityTestSupport.LoadEventBySortableUniqueId(Kanyusha10002RelatedKeiyakuReplaceSortableUniqueId);

        KanyushaListProjection nativeState =
            KenbaiWasmParityTestSupport.ReplayKanyushaListProjectionNative(checkpointRows.Concat([nendoRow, keiyakuRow]));
        string nativeJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            JsonSerializer.Serialize(nativeState, SerializerOptions));

        string checkpointSnapshot = KenbaiWasmParityTestSupport.ReplayProjectionWasm(
            KanyushaListProjection.MultiProjectorName,
            checkpointRows);

        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        int instanceId = wasm.TryCreateInstance(KanyushaListProjection.MultiProjectorName);
        Assert.True(instanceId > 0, $"Failed to create {KanyushaListProjection.MultiProjectorName}: {instanceId}");

        wasm.RestoreState(instanceId, checkpointSnapshot);
        ApplyBufferedEvent(wasm, instanceId, nendoRow);
        ApplyBufferedEvent(wasm, instanceId, keiyakuRow);

        string wasmJson = KenbaiWasmParityTestSupport.ExtractCanonicalStateJsonFromRawSnapshot(
            wasm.SerializeState(instanceId));

        string nativeAffectedItemJson = ExtractKanyushaItemJson(nativeJson, "10002");
        string wasmAffectedItemJson = ExtractKanyushaItemJson(wasmJson, "10002");

        Assert.Equal(nativeAffectedItemJson, wasmAffectedItemJson);
    }

    private static void AssertProjectionParity<TProjection>(
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows,
        TProjection nativeState,
        string wasmStateJson)
    {
        string nativeJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            JsonSerializer.Serialize(nativeState, SerializerOptions));
        string wasmJson = KenbaiWasmParityTestSupport.CanonicalizeJson(wasmStateJson);

        if (string.Equals(nativeJson, wasmJson, StringComparison.Ordinal))
        {
            return;
        }

        string firstDifferingKanyushaNo = FindFirstDifferingKanyushaNo(nativeJson, wasmJson) ?? "<unknown>";
        string? nativeItemJson = TryExtractKanyushaItemJson(nativeJson, firstDifferingKanyushaNo);
        string? wasmItemJson = TryExtractKanyushaItemJson(wasmJson, firstDifferingKanyushaNo);
        (string path, string? nativeValue, string? wasmValue)? firstDifference = FindFirstJsonDifference(
            nativeItemJson is null ? null : JsonNode.Parse(nativeItemJson),
            wasmItemJson is null ? null : JsonNode.Parse(wasmItemJson),
            "$");

        Assert.True(
            false,
            $"Projection parity mismatch after {rows.Count} events.{Environment.NewLine}" +
            $"First={rows.First().SortableUniqueId} Last={rows.Last().SortableUniqueId}{Environment.NewLine}" +
            $"FirstDifferingKanyushaNo={firstDifferingKanyushaNo}{Environment.NewLine}" +
            $"FirstDifferingPath={firstDifference?.path ?? "<unknown>"}{Environment.NewLine}" +
            $"NativeValue={firstDifference?.nativeValue ?? "<missing>"}{Environment.NewLine}" +
            $"WasmValue={firstDifference?.wasmValue ?? "<missing>"}");
    }

    private static string ExtractKanyushaItemJson(string projectionJson, string kanyushaNo)
    {
        JsonNode root = JsonNode.Parse(projectionJson)
            ?? throw new InvalidOperationException("Failed to parse projection JSON.");
        JsonNode item = root["kanyushaItems"]?[kanyushaNo]
            ?? throw new InvalidOperationException($"KanyushaItems['{kanyushaNo}'] was not found.");
        return item.ToJsonString();
    }

    private static void ApplyBufferedEvent(
        KenbaiWasmParityTestSupport.WasmDiagnosticsClient wasm,
        int instanceId,
        KenbaiWasmParityTestSupport.EventRow row)
    {
        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, KenbaiWasmParityTestSupport.CanonicalizeJson(row.TagsJson));
        wasm.ApplyBufferedEventWithSortable(instanceId, row.EventType, row.SortableUniqueId);
    }

    private static string? TryExtractKanyushaItemJson(string projectionJson, string kanyushaNo)
    {
        JsonNode? root = JsonNode.Parse(projectionJson);
        return root?["kanyushaItems"]?[kanyushaNo]?.ToJsonString();
    }

    private static string? FindFirstDifferingKanyushaNo(string nativeJson, string wasmJson)
    {
        JsonObject nativeItems = JsonNode.Parse(nativeJson)?["kanyushaItems"]?.AsObject()
            ?? throw new InvalidOperationException("Native snapshot does not contain kanyushaItems.");
        JsonObject wasmItems = JsonNode.Parse(wasmJson)?["kanyushaItems"]?.AsObject()
            ?? throw new InvalidOperationException("Wasm snapshot does not contain kanyushaItems.");

        foreach (KeyValuePair<string, JsonNode?> pair in nativeItems.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            string? wasmItemJson = wasmItems[pair.Key]?.ToJsonString();
            string nativeItemJson = pair.Value?.ToJsonString() ?? "null";
            if (!string.Equals(nativeItemJson, wasmItemJson, StringComparison.Ordinal))
            {
                return pair.Key;
            }
        }

        foreach (KeyValuePair<string, JsonNode?> pair in wasmItems.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            if (!nativeItems.ContainsKey(pair.Key))
            {
                return pair.Key;
            }
        }

        return null;
    }

    private static (string path, string? nativeValue, string? wasmValue)? FindFirstJsonDifference(
        JsonNode? nativeNode,
        JsonNode? wasmNode,
        string path)
    {
        if (nativeNode is null || wasmNode is null)
        {
            return !JsonNode.DeepEquals(nativeNode, wasmNode)
                ? (path, nativeNode?.ToJsonString(), wasmNode?.ToJsonString())
                : null;
        }

        if (nativeNode is JsonObject nativeObject && wasmNode is JsonObject wasmObject)
        {
            foreach (string key in nativeObject.Select(static x => x.Key).Union(wasmObject.Select(static x => x.Key)).OrderBy(static x => x, StringComparer.Ordinal))
            {
                (string path, string? nativeValue, string? wasmValue)? nested = FindFirstJsonDifference(
                    nativeObject[key],
                    wasmObject[key],
                    $"{path}.{key}");
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        if (nativeNode is JsonArray nativeArray && wasmNode is JsonArray wasmArray)
        {
            int maxLength = Math.Max(nativeArray.Count, wasmArray.Count);
            for (int i = 0; i < maxLength; i++)
            {
                JsonNode? nativeChild = i < nativeArray.Count ? nativeArray[i] : null;
                JsonNode? wasmChild = i < wasmArray.Count ? wasmArray[i] : null;
                (string path, string? nativeValue, string? wasmValue)? nested = FindFirstJsonDifference(
                    nativeChild,
                    wasmChild,
                    $"{path}[{i}]");
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        return !JsonNode.DeepEquals(nativeNode, wasmNode)
            ? (path, nativeNode.ToJsonString(), wasmNode.ToJsonString())
            : null;
    }
}
