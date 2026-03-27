using System.Text.Json;
using Aic.Kenbai.EventSource.Wasm;
using Aic.Kenbai.EventSource.Projections.HokenNendoShosais;
using Aic.Kenbai.EventSource.Projections.Kanyushyas;
using Sekiban.Dcb.Domains;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class KenbaiWasmRuntimeDiagnosticsTests
{
    private const string ProblematicApplicationSubmittedSortableUniqueId = "063907072027952549901977492386";

    [Fact]
    public void WasmDebugEnvironmentFlags_ShouldMatchHostEnvironment()
    {
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        int expected = 0;
        string? submittedTagMode = Environment.GetEnvironmentVariable("KENBAI_WASM_DEBUG_SUBMITTED_TAG_MODE");
        if (!string.IsNullOrWhiteSpace(submittedTagMode))
        {
            expected |= 1;
        }

        if (string.Equals(submittedTagMode, "skip-project", StringComparison.Ordinal))
        {
            expected |= 2;
        }

        if (string.Equals(
                Environment.GetEnvironmentVariable("SEKIBAN_WASM_DEBUG_BYPASS_MULTI_PROJECT"),
                "1",
                StringComparison.Ordinal))
        {
            expected |= 4;
        }

        Assert.Equal(expected, wasm.DebugEnvironmentFlags());
    }

    [Fact]
    public async Task WasmBufferedSortableApplyStage_ShouldComplete_WhenMultiProjectDispatchIsBypassed()
    {
        KenbaiWasmParityTestSupport.EventRow row =
            KenbaiWasmParityTestSupport.LoadEventBySortableUniqueId(ProblematicApplicationSubmittedSortableUniqueId);
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        Assert.Equal(0, wasm.DebugGetMultiProjectBypass());
        Assert.Equal(1, wasm.DebugSetMultiProjectBypass(true));

        try
        {
            int instanceId = wasm.TryCreateInstance(KanyushaListProjection.MultiProjectorName);
            Assert.True(instanceId > 0, $"Failed to create {KanyushaListProjection.MultiProjectorName}: {instanceId}");

            wasm.BeginPayloadBuffer(instanceId);
            wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
            wasm.BeginMetadataBuffer(instanceId);
            wasm.AppendMetadataChunk(instanceId, KenbaiWasmParityTestSupport.CanonicalizeJson(row.TagsJson));

            int applyMultiResult = await InvokeWithTimeout(
                () => wasm.DebugBufferedSortableApplyMultiEvent(instanceId, row.EventType, row.SortableUniqueId),
                "debug_buffered_sortable_apply_multi_event");
            Assert.Equal(4, applyMultiResult);
        }
        finally
        {
            wasm.DebugSetMultiProjectBypass(false);
        }
    }

    [Theory]
    [InlineData(1, 1, "kanyusha-only")]
    [InlineData(2, 1, "nendo-only")]
    [InlineData(3, 1, "keiyaku-only")]
    [InlineData(4, 2, "kanyusha-nendo")]
    [InlineData(6, 0, "skip-project")]
    [InlineData(7, 2, "kanyusha-keiyaku")]
    [InlineData(8, 2, "nendo-keiyaku")]
    public async Task WasmBufferedSortableApplyStage_ShouldIsolateSubmittedTagLoops(
        int submittedTagMode,
        int expectedTagCount,
        string modeName)
    {
        KenbaiWasmParityTestSupport.EventRow row =
            KenbaiWasmParityTestSupport.LoadEventBySortableUniqueId(ProblematicApplicationSubmittedSortableUniqueId);
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        Assert.Equal(submittedTagMode, wasm.DebugSetSubmittedTagMode(submittedTagMode));
        wasm.DebugClearLastMultiProjectError();

        try
        {
            int instanceId = wasm.TryCreateInstance(KanyushaListProjection.MultiProjectorName);
            Assert.True(instanceId > 0, $"Failed to create {KanyushaListProjection.MultiProjectorName}: {instanceId}");

            wasm.BeginPayloadBuffer(instanceId);
            wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
            wasm.BeginMetadataBuffer(instanceId);
            wasm.AppendMetadataChunk(instanceId, KenbaiWasmParityTestSupport.CanonicalizeJson(row.TagsJson));
            int resolveTagObjectsResult = await InvokeWithTimeout(
                () => wasm.DebugBufferedSortableResolveTagObjects(instanceId, row.EventType, row.SortableUniqueId),
                $"debug_buffered_sortable_resolve_tag_objects:{modeName}");
            Assert.Equal(expectedTagCount, resolveTagObjectsResult);

            wasm.BeginPayloadBuffer(instanceId);
            wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
            wasm.BeginMetadataBuffer(instanceId);
            wasm.AppendMetadataChunk(instanceId, KenbaiWasmParityTestSupport.CanonicalizeJson(row.TagsJson));

            int applyMultiResult = await InvokeWithTimeout(
                () => wasm.DebugBufferedSortableApplyMultiEvent(instanceId, row.EventType, row.SortableUniqueId),
                $"debug_buffered_sortable_apply_multi_event:{modeName}");
            if (applyMultiResult < 0)
            {
                string lastError = wasm.DebugTakeLastMultiProjectError();
                Assert.Fail($"WASM multi project failed for {modeName}: code={applyMultiResult}, error={lastError}");
            }
            Assert.Equal(expectedTagCount, applyMultiResult);
        }
        finally
        {
            wasm.DebugClearLastMultiProjectError();
            wasm.DebugSetSubmittedTagMode(0);
        }
    }

    [Fact]
    public void WasmDomainTypes_ShouldUseAotWithoutResultMultiProjectorTypes_ForKenbaiProjectors()
    {
        Assert.IsType<AotWithoutResultMultiProjectorTypes>(KenbaiWasmParityTestSupport.WasmDomainTypes.MultiProjectorTypes);
    }

    [Fact]
    public async Task NativeProjection_ShouldHandleProblematicApplicationSubmittedEventWithinTimeout()
    {
        KenbaiWasmParityTestSupport.EventRow row =
            KenbaiWasmParityTestSupport.LoadEventBySortableUniqueId(ProblematicApplicationSubmittedSortableUniqueId);

        KanyushaListProjection result = await InvokeWithTimeout(
            () => KenbaiWasmParityTestSupport.ReplayKanyushaListProjectionNative([row]),
            "native_kanyusha_list_projection_project");

        Assert.NotNull(result);
    }

    [Fact]
    public void WasmModule_ShouldExposeCoreProjectionRuntimeExports()
    {
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        IReadOnlyList<string> exportNames = wasm.ExportNames;
        int kanyushaListInstanceId = wasm.TryCreateInstance(KanyushaListProjection.MultiProjectorName);
        int hokenInstanceId = wasm.TryCreateInstance(HokenNendoShosaiListProjection.MultiProjectorName);
        int tagInstanceId = wasm.TryCreateInstance("KanyushaNumberKanriTagProjector");

        Assert.Contains("create_instance", exportNames);
        Assert.Contains("execute_list_query", exportNames);
        Assert.Contains("serialize_state", exportNames);
        Assert.Contains("apply_event_with_metadata", exportNames);
        Assert.Contains("apply_buffered_event_with_metadata", exportNames);
        Assert.True(
            kanyushaListInstanceId > 0,
            $"Expected {KanyushaListProjection.MultiProjectorName} to be creatable, but create_instance returned {kanyushaListInstanceId}. Exports=[{string.Join(", ", exportNames)}]");
        Assert.True(
            hokenInstanceId > 0,
            $"Expected {HokenNendoShosaiListProjection.MultiProjectorName} to be creatable, but create_instance returned {hokenInstanceId}. Exports=[{string.Join(", ", exportNames)}]");
        Assert.True(
            tagInstanceId > 0,
            $"Expected KanyushaNumberKanriTagProjector to be creatable, but create_instance returned {tagInstanceId}. Exports=[{string.Join(", ", exportNames)}]");
    }

    [Fact]
    public void WasmModule_ShouldExposeDiagnosticRoundTripExports()
    {
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        IReadOnlyList<string> exportNames = wasm.ExportNames;

        Assert.Contains("diagnose_domain_types", exportNames);
        Assert.Contains("debug_roundtrip_event_payload", exportNames);
        Assert.Contains("debug_roundtrip_tags", exportNames);
    }

    [Fact]
    public void WasmTagRoundTrip_ShouldMatchCanonicalJson_ForDbSamples()
    {
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        if (!wasm.SupportsDebugRoundTrip)
        {
            return;
        }

        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadEventSamplesPerType(sampleCountPerType: 2);

        foreach (KenbaiWasmParityTestSupport.EventRow row in rows)
        {
            string expected = KenbaiWasmParityTestSupport.CanonicalizeJson(row.TagsJson);
            string actual = KenbaiWasmParityTestSupport.CanonicalizeJson(wasm.RoundTripTags(row.TagsJson));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void WasmEventPayloadRoundTrip_ShouldMatchNativeSerialization_ForDbSamples()
    {
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        if (!wasm.SupportsDebugRoundTrip)
        {
            return;
        }

        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadEventSamplesPerType(sampleCountPerType: 2);
        var mismatches = new List<string>();

        foreach (KenbaiWasmParityTestSupport.EventRow row in rows)
        {
            Type payloadType = KenbaiWasmParityTestSupport.NativeDomainTypes.EventTypes.GetEventType(row.EventType)
                ?? throw new InvalidOperationException($"Native domain does not resolve event type '{row.EventType}'.");
            object payload = JsonSerializer.Deserialize(
                row.PayloadJson,
                payloadType,
                KenbaiWasmParityTestSupport.NativeDomainTypes.JsonSerializerOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{row.EventType}' natively.");

            string expectedJson = KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
                JsonSerializer.Serialize(
                    payload,
                    payloadType,
                    KenbaiWasmParityTestSupport.NativeDomainTypes.JsonSerializerOptions));
            string artifactJson = KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
                wasm.RoundTripEventPayload(row.EventType, row.PayloadJson));

            if (!string.Equals(expectedJson, artifactJson, StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"{row.EventType}@{row.SortableUniqueId}:{Environment.NewLine}" +
                    $"expected(native)={expectedJson}{Environment.NewLine}" +
                    $"artifact={artifactJson}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"Artifact payload roundtrip differs from native runtime serialization:{Environment.NewLine}{string.Join($"{Environment.NewLine}{Environment.NewLine}", mismatches)}");
    }

    [Fact]
    public void WasmEventPayloadRoundTrip_ShouldMatchNativeSerialization_ForRepresentativeEvents()
    {
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        if (!wasm.SupportsDebugRoundTrip)
        {
            return;
        }

        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadEventSamplesPerType(sampleCountPerType: 1);
        var mismatches = new List<string>();

        foreach (KenbaiWasmParityTestSupport.EventRow row in rows)
        {
            Type payloadType = KenbaiWasmParityTestSupport.NativeDomainTypes.EventTypes.GetEventType(row.EventType)
                ?? throw new InvalidOperationException($"Native domain does not resolve event type '{row.EventType}'.");

            object payload = JsonSerializer.Deserialize(
                row.PayloadJson,
                payloadType,
                KenbaiWasmParityTestSupport.NativeDomainTypes.JsonSerializerOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{row.EventType}' natively.");

            string expected = KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
                JsonSerializer.Serialize(
                    payload,
                    payloadType,
                    KenbaiWasmParityTestSupport.NativeDomainTypes.JsonSerializerOptions));
            string sourceWasm = KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
                KenbaiWasmParityTestSupport.WasmDomainTypes.EventTypes.SerializeEventPayload(
                    (Sekiban.Dcb.Events.IEventPayload)payload));
            string artifact = KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
                wasm.RoundTripEventPayload(row.EventType, row.PayloadJson));

            if (!string.Equals(expected, artifact, StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"{row.EventType}@{row.SortableUniqueId}:{Environment.NewLine}" +
                    $"expected(native)={expected}{Environment.NewLine}" +
                    $"source-wasm={sourceWasm}{Environment.NewLine}" +
                    $"artifact={artifact}{Environment.NewLine}" +
                    $"jsonTypeInfo={(AicDomainJsonContext.Default.GetTypeInfo(payloadType) is null ? "<null>" : payloadType.FullName)}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"Artifact payload roundtrip differs from native/source serialization:{Environment.NewLine}{string.Join($"{Environment.NewLine}{Environment.NewLine}", mismatches)}");
    }

    [Fact]
    public void WasmEventPayloadRoundTrip_ShouldMatchSourceWasmSerialization_ForRepresentativeEvents()
    {
        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        if (!wasm.SupportsDebugRoundTrip)
        {
            return;
        }

        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadEventSamplesPerType(sampleCountPerType: 1);
        var mismatches = new List<string>();

        foreach (KenbaiWasmParityTestSupport.EventRow row in rows)
        {
            Sekiban.Dcb.Events.IEventPayload sourcePayload =
                KenbaiWasmParityTestSupport.WasmDomainTypes.EventTypes.DeserializeEventPayload(row.EventType, row.PayloadJson)
                ?? throw new InvalidOperationException($"Source WASM domain does not resolve event type '{row.EventType}'.");

            string sourceWasm = KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
                KenbaiWasmParityTestSupport.WasmDomainTypes.EventTypes.SerializeEventPayload(sourcePayload));
            string artifact = KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
                wasm.RoundTripEventPayload(row.EventType, row.PayloadJson));

            if (!string.Equals(sourceWasm, artifact, StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"{row.EventType}@{row.SortableUniqueId}:{Environment.NewLine}" +
                    $"source-wasm={sourceWasm}{Environment.NewLine}" +
                    $"artifact={artifact}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"Artifact payload roundtrip differs from source-level WASM domain serialization:{Environment.NewLine}{string.Join($"{Environment.NewLine}{Environment.NewLine}", mismatches)}");
    }

    [Fact]
    public async Task WasmEventPayloadRoundTrip_ShouldHandleProblematicApplicationSubmittedPayloadWithinTimeout()
    {
        KenbaiWasmParityTestSupport.EventRow row =
            KenbaiWasmParityTestSupport.LoadEventBySortableUniqueId(ProblematicApplicationSubmittedSortableUniqueId);

        Assert.Equal("KanyushaWebServiceApplicationSubmitted", row.EventType);

        Type payloadType = KenbaiWasmParityTestSupport.NativeDomainTypes.EventTypes.GetEventType(row.EventType)
            ?? throw new InvalidOperationException($"Native domain does not resolve event type '{row.EventType}'.");
        object payload = JsonSerializer.Deserialize(
            row.PayloadJson,
            payloadType,
            KenbaiWasmParityTestSupport.NativeDomainTypes.JsonSerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize '{row.EventType}' natively.");
        string expected = KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
            JsonSerializer.Serialize(
                payload,
                payloadType,
                KenbaiWasmParityTestSupport.NativeDomainTypes.JsonSerializerOptions));

        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        TimeSpan timeout = TimeSpan.FromSeconds(30);

        Task<string> sourceTask = Task.Run(() =>
        {
            var sourcePayload = KenbaiWasmParityTestSupport.WasmDomainTypes.EventTypes.DeserializeEventPayload(row.EventType, row.PayloadJson)
                ?? throw new InvalidOperationException($"Source WASM domain failed to deserialize '{row.EventType}'.");
            return KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
                KenbaiWasmParityTestSupport.WasmDomainTypes.EventTypes.SerializeEventPayload(sourcePayload));
        });

        Task<string> artifactTask = Task.Run(() =>
            KenbaiWasmParityTestSupport.CanonicalizeJsonForRuntimeParity(
                wasm.RoundTripEventPayload(row.EventType, row.PayloadJson)));

        Task completedTask = await Task.WhenAny(Task.WhenAll(sourceTask, artifactTask), Task.Delay(timeout));
        Assert.NotEqual(Task.Delay(timeout), completedTask);

        string source = await sourceTask;
        string artifact = await artifactTask;

        Assert.Equal(expected, source);
        Assert.Equal(expected, artifact);
    }

    [Fact]
    public async Task WasmBufferedSortableDiagnostics_ShouldIsolateMetadataStage_ForProblematicEvent()
    {
        KenbaiWasmParityTestSupport.EventRow row =
            KenbaiWasmParityTestSupport.LoadEventBySortableUniqueId(ProblematicApplicationSubmittedSortableUniqueId);

        Assert.Equal("KanyushaWebServiceApplicationSubmitted", row.EventType);

        using var wasm = new KenbaiWasmParityTestSupport.WasmDiagnosticsClient();
        Assert.True(wasm.HasRawExport("debug_buffered_sortable_noop"));
        Assert.True(wasm.HasRawExport("debug_buffered_sortable_deserialize_payload"));
        Assert.True(wasm.HasRawExport("debug_buffered_sortable_parse_tags"));
        Assert.True(wasm.HasRawExport("debug_buffered_sortable_create_event"));
        Assert.True(wasm.HasRawExport("debug_buffered_sortable_resolve_tag_objects"));
        Assert.True(wasm.HasRawExport("debug_buffered_sortable_resolve_safe_window"));
        Assert.True(wasm.HasRawExport("debug_buffered_sortable_apply_multi_event"));
        Assert.True(wasm.CanWrapFiveIntParamsReturningInt("debug_buffered_sortable_noop"));
        Assert.True(wasm.CanWrapFiveIntParamsReturningInt("debug_buffered_sortable_deserialize_payload"));
        Assert.True(wasm.CanWrapFiveIntParamsReturningInt("debug_buffered_sortable_parse_tags"));
        Assert.True(wasm.CanWrapFiveIntParamsReturningInt("debug_buffered_sortable_create_event"));
        Assert.True(wasm.CanWrapFiveIntParamsReturningInt("debug_buffered_sortable_resolve_tag_objects"));
        Assert.True(wasm.CanWrapFiveIntParamsReturningInt("debug_buffered_sortable_resolve_safe_window"));
        Assert.True(wasm.CanWrapFiveIntParamsReturningInt("debug_buffered_sortable_apply_multi_event"));
        int instanceId = wasm.TryCreateInstance(KanyushaListProjection.MultiProjectorName);
        Assert.True(instanceId > 0, $"Failed to create {KanyushaListProjection.MultiProjectorName}: {instanceId}");

        string tagsJson = KenbaiWasmParityTestSupport.CanonicalizeJson(row.TagsJson);
        int expectedTagsLength = tagsJson.Length;
        int expectedHeaderLength = expectedTagsLength + row.EventType.Length + row.SortableUniqueId.Length;
        int expectedPayloadStageLength = expectedTagsLength + row.PayloadJson.Length;

        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        Assert.Equal(expectedTagsLength, wasm.DebugMetadataBufferLength(instanceId));

        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        int noopResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableNoop(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_noop");
        Assert.Equal(1, noopResult);

        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        int metadataResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableConsumeMetadata(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_consume_metadata");
        Assert.Equal(expectedTagsLength, metadataResult);

        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        int headerResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableConsumeMetadataAndReadHeaders(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_consume_metadata_and_read_headers");
        Assert.Equal(expectedHeaderLength, headerResult);

        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        int payloadResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableConsumeMetadataAndPayload(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_consume_metadata_and_payload");
        Assert.Equal(expectedPayloadStageLength, payloadResult);

        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
        int deserializeResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableDeserializePayload(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_deserialize_payload");
        Assert.Equal(row.PayloadJson.Length, deserializeResult);

        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        int parseTagsResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableParseTags(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_parse_tags");
        Assert.Equal(4, parseTagsResult);

        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        int createEventResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableCreateEvent(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_create_event");
        Assert.Equal(4, createEventResult);

        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        int resolveTagObjectsResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableResolveTagObjects(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_resolve_tag_objects");
        Assert.InRange(resolveTagObjectsResult, 0, 4);

        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        int resolveSafeWindowResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableResolveSafeWindow(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_resolve_safe_window");
        Assert.True(resolveSafeWindowResult >= resolveTagObjectsResult);

        wasm.BeginPayloadBuffer(instanceId);
        wasm.AppendPayloadChunk(instanceId, row.PayloadJson);
        wasm.BeginMetadataBuffer(instanceId);
        wasm.AppendMetadataChunk(instanceId, tagsJson);
        int applyMultiResult = await InvokeWithTimeout(
            () => wasm.DebugBufferedSortableApplyMultiEvent(instanceId, row.EventType, row.SortableUniqueId),
            "debug_buffered_sortable_apply_multi_event");
        Assert.Equal(4, applyMultiResult);
    }

    private static async Task<T> InvokeWithTimeout<T>(Func<T> operation, string operationName)
    {
        Task<T> task = Task.Run(operation);
        Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(ReferenceEquals(completed, task), $"Timed out while invoking {operationName}.");
        return await task;
    }
}
