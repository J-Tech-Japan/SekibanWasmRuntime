using System.Text;
using Aic.Kenbai.EventSource.KanyushaQuestionnaires;
using Aic.Kenbai.EventSource.Kanyushas;
using Aic.Kenbai.EventSource.NendoKanyushas;
using Aic.Kenbai.EventSource.SenkoKenchikushis;
using Aic.Kenbai.EventSource.KanyushaNos;
using Aic.Kenbai.ImmutableModels.Events.KanyushaNendoKaishis;
using Aic.Kenbai.ImmutableModels.Events.KanyushaNos;
using Aic.Kenbai.ImmutableModels.Events.Kanyushas;
using Aic.Kenbai.ImmutableModels.Events.SenkoKenchikushis;
using Sekiban.Dcb;
using Sekiban.Dcb.Tags;
using Xunit;
using System.Text.Json.Nodes;

namespace SekibanWasm.Cs.Tests;

public class KenbaiWasmTagStateParityTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    public void KanyushaNumberKanriTagState_ShouldMatchNativeAndWasm(int takeCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEventsByTypes(
                takeCount,
                nameof(KanyushaNumberHaraidashiInitialized),
                nameof(KanyushaNumberHaraidashiSucceeded));

        Assert.NotEmpty(rows);

        ITagStatePayload nativeState = KenbaiWasmParityTestSupport.ReplayKanyushaNumberKanriTagStateNative(rows);
        Assert.IsType<KanyushaNumberKanriState>(nativeState);

        string nativeJson = SerializeCanonicalTagState(KenbaiWasmParityTestSupport.NativeDomainTypes, nativeState);
        string wasmJson = SerializeCanonicalTagState(KenbaiWasmParityTestSupport.WasmDomainTypes, nativeState);

        Assert.Equal(nativeJson, wasmJson);

        string wasmRuntimeJson = KenbaiWasmParityTestSupport.ExtractCanonicalStateJsonFromRawSnapshot(
            KenbaiWasmParityTestSupport.ReplayProjectionWasm(
                nameof(KanyushaNumberKanriTagProjector),
                rows));
        Assert.Equal(nativeJson, wasmRuntimeJson);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(20, 10)]
    public void KanyushaNumberKanriTagState_Restore_ShouldMatchNativeAndWasm(
        int takeCount,
        int restoreAfterCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEventsByTypes(
                takeCount,
                nameof(KanyushaNumberHaraidashiInitialized),
                nameof(KanyushaNumberHaraidashiSucceeded));

        ITagStatePayload nativeState = KenbaiWasmParityTestSupport.ReplayKanyushaNumberKanriTagStateNative(rows);
        string nativeJson = SerializeCanonicalTagState(KenbaiWasmParityTestSupport.NativeDomainTypes, nativeState);

        string wasmRuntimeJson = KenbaiWasmParityTestSupport.ExtractCanonicalStateJsonFromRawSnapshot(
            KenbaiWasmParityTestSupport.ReplayProjectionWasmWithRestore(
                nameof(KanyushaNumberKanriTagProjector),
                rows,
                restoreAfterCount));

        Assert.Equal(nativeJson, wasmRuntimeJson);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    public void KanyushaQuestionnaireGroupTagState_ShouldMatchNativeAndWasm(int takeCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEventsByTag(
                takeCount,
                "KanyushaQuestionnaireGroup:14_Shinki_Web");

        Assert.NotEmpty(rows);

        ITagStatePayload nativeState = KenbaiWasmParityTestSupport.ReplayTagStateNative(
            nameof(KanyushaQuestionnaireGroupProjector),
            rows);
        Assert.IsType<KanyushaQuestionnaireGroupState>(nativeState);

        string nativeJson = SerializeCanonicalTagState(KenbaiWasmParityTestSupport.NativeDomainTypes, nativeState);
        string wasmJson = SerializeCanonicalTagState(KenbaiWasmParityTestSupport.WasmDomainTypes, nativeState);
        Assert.Equal(nativeJson, wasmJson);

        string wasmRuntimeJson = KenbaiWasmParityTestSupport.ExtractCanonicalStateJsonFromRawSnapshot(
            KenbaiWasmParityTestSupport.ReplayProjectionWasm(
                nameof(KanyushaQuestionnaireGroupProjector),
                rows));
        Assert.Equal(nativeJson, wasmRuntimeJson);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    public void SenkoKenchikushiYearlyTagState_ShouldMatchNativeAndWasm(int takeCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEventsByTag(
                takeCount,
                "SenkoKenchikushiYearly:14");

        Assert.NotEmpty(rows);

        ITagStatePayload nativeState = KenbaiWasmParityTestSupport.ReplayTagStateNative(
            nameof(SenkoKenchikushiYearlyProjector),
            rows);
        Assert.IsType<SenkoKenchikushiYearlyState>(nativeState);

        string nativeJson = SerializeCanonicalTagState(KenbaiWasmParityTestSupport.NativeDomainTypes, nativeState);
        string wasmJson = SerializeCanonicalTagState(KenbaiWasmParityTestSupport.WasmDomainTypes, nativeState);
        Assert.Equal(nativeJson, wasmJson);

        string wasmRuntimeJson = KenbaiWasmParityTestSupport.ExtractCanonicalStateJsonFromRawSnapshot(
            KenbaiWasmParityTestSupport.ReplayProjectionWasm(
                nameof(SenkoKenchikushiYearlyProjector),
                rows));
        Assert.Equal(nativeJson, wasmRuntimeJson);
    }

    [Fact]
    public void KanyushaQuestionnaireGroupTagState_WasmRawSnapshot_ShouldExposeNonEmptyState()
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEventsByTag(
                5,
                "KanyushaQuestionnaireGroup:14_Shinki_Web");

        string rawSnapshot = KenbaiWasmParityTestSupport.ReplayProjectionWasm(
            nameof(KanyushaQuestionnaireGroupProjector),
            rows);

        JsonNode? snapshot = JsonNode.Parse(rawSnapshot);
        Assert.NotNull(snapshot);
        Assert.Equal(nameof(KanyushaQuestionnaireGroupProjector), snapshot?["tagProjector"]?.GetValue<string>());
        Assert.Equal(nameof(KanyushaQuestionnaireGroupState), snapshot?["tagPayloadName"]?.GetValue<string>());
        Assert.NotEqual("{}", snapshot?["stateJson"]?.GetValue<string>());
    }

    [Fact]
    public void KanyushaQuestionnaireGroupTagState_GuestDirectProjection_ShouldExposeNonEmptyState()
    {
        KenbaiWasmParityTestSupport.EventRow row =
            KenbaiWasmParityTestSupport.LoadOrderedEventsByTag(
                1,
                "KanyushaQuestionnaireGroup:14_Shinki_Web")
            .Single();

        using var diagnostics = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        string rawSnapshot = diagnostics.DebugProjectOnce(
            nameof(KanyushaQuestionnaireGroupProjector),
            row.EventType,
            row.PayloadJson,
            row.TagsJson,
            row.SortableUniqueId);

        JsonNode? snapshot = JsonNode.Parse(rawSnapshot);
        Assert.NotNull(snapshot);
        Assert.Equal(nameof(KanyushaQuestionnaireGroupProjector), snapshot?["tagProjector"]?.GetValue<string>());
        Assert.Equal(nameof(KanyushaQuestionnaireGroupState), snapshot?["tagPayloadName"]?.GetValue<string>());
        Assert.NotEqual("{}", snapshot?["stateJson"]?.GetValue<string>());
    }

    [Fact]
    public void KanyushaTagState_WasmPrimitive_ShouldMatchNativeAndRestoreFromNonEmptyCache()
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEventsByTag(
                10,
                "Kanyusha:12003");

        Assert.Equal(
            nameof(KanyushaWebServiceApplicationSubmitted),
            rows[0].EventType);

        ITagStatePayload nativeState = KenbaiWasmParityTestSupport.ReplayTagStateNative(
            nameof(KanyushaProjector),
            rows);
        Assert.IsType<KanyushaState>(nativeState);

        string nativeJson = SerializeCanonicalTagState(
            KenbaiWasmParityTestSupport.NativeDomainTypes,
            nativeState);

        SerializableTagState wasmState = KenbaiWasmParityTestSupport.ReplayTagStateWasmPrimitive(
            nameof(KanyushaProjector),
            rows);

        Assert.Equal(nameof(KanyushaState), wasmState.TagPayloadName);
        Assert.Equal(rows[^1].SortableUniqueId, wasmState.LastSortedUniqueId);
        Assert.Equal(rows.Count, wasmState.Version);

        string wasmJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            Encoding.UTF8.GetString(wasmState.Payload));
        Assert.Equal(nativeJson, wasmJson);

        SerializableTagState restoredState = KenbaiWasmParityTestSupport.ReplayTagStateWasmPrimitive(
            nameof(KanyushaProjector),
            [],
            wasmState,
            wasmState.LastSortedUniqueId);

        Assert.Equal(nameof(KanyushaState), restoredState.TagPayloadName);
        Assert.Equal(wasmState.Version, restoredState.Version);
        Assert.Equal(wasmState.LastSortedUniqueId, restoredState.LastSortedUniqueId);
        Assert.Equal(wasmJson, KenbaiWasmParityTestSupport.CanonicalizeJson(Encoding.UTF8.GetString(restoredState.Payload)));
    }

    [Fact]
    public void KanyushaTagState_WasmPrimitive_ShouldRestoreWebApplicationAndAccountLoginShape()
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEventsByTag(
                2,
                "Kanyusha:12001");

        Assert.Equal(2, rows.Count);
        Assert.Equal(nameof(KanyushaWebServiceApplicationSubmitted), rows[0].EventType);
        Assert.Equal(nameof(KanyushaAccountLoginCreatedAndPasswordChanged), rows[1].EventType);

        ITagStatePayload nativeState = KenbaiWasmParityTestSupport.ReplayTagStateNative(
            nameof(KanyushaProjector),
            rows);
        Assert.IsType<KanyushaState>(nativeState);

        string nativeJson = SerializeCanonicalTagState(
            KenbaiWasmParityTestSupport.NativeDomainTypes,
            nativeState);

        SerializableTagState wasmState = KenbaiWasmParityTestSupport.ReplayTagStateWasmPrimitive(
            nameof(KanyushaProjector),
            rows.Take(1));

        Assert.Equal(nameof(KanyushaState), wasmState.TagPayloadName);

        var deserializeResult = KenbaiWasmParityTestSupport.WasmDomainTypes.TagStatePayloadTypes.DeserializePayload(
            wasmState.TagPayloadName,
            wasmState.Payload);
        Assert.True(
            deserializeResult.IsSuccess,
            deserializeResult.IsSuccess ? string.Empty : deserializeResult.GetException().ToString());
        Assert.IsType<KanyushaState>(deserializeResult.GetValue());

        SerializableTagState restoredState = KenbaiWasmParityTestSupport.ReplayTagStateWasmPrimitive(
            nameof(KanyushaProjector),
            rows.Skip(1),
            wasmState,
            rows[^1].SortableUniqueId);

        Assert.Equal(nameof(KanyushaState), restoredState.TagPayloadName);
        Assert.Equal(rows[^1].SortableUniqueId, restoredState.LastSortedUniqueId);
        Assert.Equal(rows.Count, restoredState.Version);
        Assert.Equal(
            NormalizeKanyushaStateJson(nativeJson),
            NormalizeKanyushaStateJson(
                KenbaiWasmParityTestSupport.CanonicalizeJson(Encoding.UTF8.GetString(restoredState.Payload))));
    }

    [Fact]
    public void NendoKanyuTagState_WasmPrimitive_ShouldMatchNativeAndRestoreFromNonEmptyCache()
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEventsByTag(
                20,
                "NendoKanyu:5281f8d4-fffd-41ce-ab03-be9d2d06e141");

        Assert.NotEmpty(rows);

        ITagStatePayload nativeState = KenbaiWasmParityTestSupport.ReplayTagStateNative(
            nameof(NendoKanyuProjector),
            rows);
        Assert.IsType<NendoKanyuState>(nativeState);

        string nativeJson = SerializeCanonicalTagState(
            KenbaiWasmParityTestSupport.NativeDomainTypes,
            nativeState);

        SerializableTagState wasmState = KenbaiWasmParityTestSupport.ReplayTagStateWasmPrimitive(
            nameof(NendoKanyuProjector),
            rows);

        Assert.Equal(nameof(NendoKanyuState), wasmState.TagPayloadName);
        Assert.Equal(rows[^1].SortableUniqueId, wasmState.LastSortedUniqueId);
        Assert.Equal(rows.Count, wasmState.Version);

        string wasmJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            Encoding.UTF8.GetString(wasmState.Payload));
        Assert.Equal(nativeJson, wasmJson);

        SerializableTagState restoredState = KenbaiWasmParityTestSupport.ReplayTagStateWasmPrimitive(
            nameof(NendoKanyuProjector),
            [],
            wasmState,
            wasmState.LastSortedUniqueId);

        Assert.Equal(nameof(NendoKanyuState), restoredState.TagPayloadName);
        Assert.Equal(wasmState.Version, restoredState.Version);
        Assert.Equal(wasmState.LastSortedUniqueId, restoredState.LastSortedUniqueId);
        Assert.Equal(
            wasmJson,
            KenbaiWasmParityTestSupport.CanonicalizeJson(Encoding.UTF8.GetString(restoredState.Payload)));
    }

    private static string SerializeCanonicalTagState(DcbDomainTypes domainTypes, ITagStatePayload state)
    {
        byte[] bytes = domainTypes.TagStatePayloadTypes.SerializePayload(state).GetValue();
        string json = Encoding.UTF8.GetString(bytes);
        return KenbaiWasmParityTestSupport.CanonicalizeJson(json);
    }

    private static string NormalizeKanyushaStateJson(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        RemovePropertiesRecursively(node, static propertyName =>
            string.Equals(propertyName, "updatedAt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(propertyName, "registeredAt", StringComparison.OrdinalIgnoreCase));
        return KenbaiWasmParityTestSupport.CanonicalizeJson(node?.ToJsonString() ?? json);
    }

    private static void RemovePropertiesRecursively(JsonNode? node, Func<string, bool> shouldRemove)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                foreach (string key in obj.Select(static pair => pair.Key).ToArray())
                {
                    if (shouldRemove(key))
                    {
                        obj.Remove(key);
                        continue;
                    }

                    RemovePropertiesRecursively(obj[key], shouldRemove);
                }

                break;
            }
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    RemovePropertiesRecursively(item, shouldRemove);
                }

                break;
        }
    }
}
