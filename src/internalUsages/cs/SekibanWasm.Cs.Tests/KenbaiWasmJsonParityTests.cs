using System.Text.Json;
using Aic.Kenbai.EventSource.Projections.HokenNendoShosais;
using Aic.Kenbai.EventSource.Projections.Kanyushyas;
using Sekiban.Dcb;
using Sekiban.Dcb.Events;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class KenbaiWasmJsonParityTests
{
    private static readonly DcbDomainTypes NativeDomainTypes = KenbaiWasmParityTestSupport.NativeDomainTypes;
    private static readonly DcbDomainTypes WasmDomainTypes = KenbaiWasmParityTestSupport.WasmDomainTypes;
    private static readonly JsonSerializerOptions NativeOptions = NativeDomainTypes.JsonSerializerOptions;
    private static readonly JsonSerializerOptions WasmOptions = KenbaiWasmParityTestSupport.WasmJsonSerializerOptions;

    [Fact]
    public void WasmEventTypeRegistry_ShouldCoverAllDbEventTypes()
    {
        IReadOnlyList<string> dbEventTypes = KenbaiWasmParityTestSupport.LoadDistinctEventTypeNamesFromDb();

        string[] missing = dbEventTypes
            .Where(eventType => WasmDomainTypes.EventTypes.GetEventType(eventType) is null)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"WASM event registry is missing DB event types: {string.Join(", ", missing)}");
    }

    [Fact]
    public void WasmJsonContext_ShouldHaveTypeInfo_ForAllDbEventPayloadTypes()
    {
        IReadOnlyList<string> dbEventTypes = KenbaiWasmParityTestSupport.LoadDistinctEventTypeNamesFromDb();

        string[] missing = dbEventTypes
            .Select(eventType => WasmDomainTypes.EventTypes.GetEventType(eventType))
            .Where(type => type is not null)
            .Distinct()
            .Where(type => KenbaiWasmParityTestSupport.GetWasmJsonTypeInfo(type!) is null)
            .Select(type => type!.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"AicDomainJsonContext is missing JsonTypeInfo for DB event payload types: {string.Join(", ", missing)}");
    }

    [Fact]
    public void WasmEventTypeRegistry_ShouldMatchNativeRegistry_ForDbEventTypes()
    {
        IReadOnlyList<string> dbEventTypes = KenbaiWasmParityTestSupport.LoadDistinctEventTypeNamesFromDb();

        var mismatches = dbEventTypes
            .Select(eventType => new
            {
                EventType = eventType,
                NativeType = NativeDomainTypes.EventTypes.GetEventType(eventType),
                WasmType = WasmDomainTypes.EventTypes.GetEventType(eventType)
            })
            .Where(x => x.NativeType != x.WasmType)
            .Select(x => $"{x.EventType}: native={x.NativeType?.FullName ?? "<null>"} wasm={x.WasmType?.FullName ?? "<null>"}")
            .ToArray();

        Assert.True(
            mismatches.Length == 0,
            $"Native/WASM event registry mismatch detected: {string.Join("; ", mismatches)}");
    }

    [Fact]
    public void EventPayloadRoundTrip_ShouldMatchNativeAndWasm_ForPerTypeSamples()
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadEventSamplesPerType(sampleCountPerType: 3);

        var mismatches = new List<string>();

        foreach (KenbaiWasmParityTestSupport.EventRow row in rows)
        {
            try
            {
                AssertEventPayloadParity(row);
            }
            catch (Exception ex)
            {
                mismatches.Add($"{row.EventType}@{row.SortableUniqueId}: {ex.Message}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"Event payload parity mismatches:{Environment.NewLine}{string.Join(Environment.NewLine, mismatches)}");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(1000)]
    [InlineData(3000)]
    public void KanyushaListProjectionStateSerialization_ShouldMatchNativeAndWasm(int takeCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEvents(takeCount);

        KanyushaListProjection state = KenbaiWasmParityTestSupport.ReplayKanyushaListProjectionNative(rows);
        AssertStateSerializationParity(state, $"KanyushaListProjection after {takeCount} events");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(1000)]
    [InlineData(3000)]
    public void HokenNendoShosaiListProjectionStateSerialization_ShouldMatchNativeAndWasm(int takeCount)
    {
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows =
            KenbaiWasmParityTestSupport.LoadOrderedEvents(takeCount);

        HokenNendoShosaiListProjection state =
            KenbaiWasmParityTestSupport.ReplayHokenNendoShosaiListProjectionNative(rows);
        AssertStateSerializationParity(state, $"HokenNendoShosaiListProjection after {takeCount} events");
    }

    private static void AssertEventPayloadParity(KenbaiWasmParityTestSupport.EventRow row)
    {
        IEventPayload nativePayload = NativeDomainTypes.EventTypes.DeserializeEventPayload(row.EventType, row.PayloadJson)
            ?? throw new InvalidOperationException("Native deserializer returned null.");
        IEventPayload wasmPayload = WasmDomainTypes.EventTypes.DeserializeEventPayload(row.EventType, row.PayloadJson)
            ?? throw new InvalidOperationException("WASM deserializer returned null.");

        Assert.True(
            nativePayload.GetType() == wasmPayload.GetType(),
            $"CLR payload types differ. native={nativePayload.GetType().FullName}, wasm={wasmPayload.GetType().FullName}");

        string nativeJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            NativeDomainTypes.EventTypes.SerializeEventPayload(nativePayload));
        string wasmJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            WasmDomainTypes.EventTypes.SerializeEventPayload(wasmPayload));

        Assert.True(
            string.Equals(nativeJson, wasmJson, StringComparison.Ordinal),
            $"Serialized payload JSON differs. native={nativeJson} wasm={wasmJson}");

        IEventPayload nativeFromWasmJson =
            NativeDomainTypes.EventTypes.DeserializeEventPayload(row.EventType, wasmJson)
            ?? throw new InvalidOperationException("Native cross-deserializer returned null.");
        IEventPayload wasmFromNativeJson =
            WasmDomainTypes.EventTypes.DeserializeEventPayload(row.EventType, nativeJson)
            ?? throw new InvalidOperationException("WASM cross-deserializer returned null.");

        string nativeCrossJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            NativeDomainTypes.EventTypes.SerializeEventPayload(nativeFromWasmJson));
        string wasmCrossJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            WasmDomainTypes.EventTypes.SerializeEventPayload(wasmFromNativeJson));

        Assert.True(
            string.Equals(nativeJson, nativeCrossJson, StringComparison.Ordinal),
            $"Native cross-roundtrip changed payload JSON. before={nativeJson} after={nativeCrossJson}");
        Assert.True(
            string.Equals(wasmJson, wasmCrossJson, StringComparison.Ordinal),
            $"WASM cross-roundtrip changed payload JSON. before={wasmJson} after={wasmCrossJson}");
    }

    private static void AssertStateSerializationParity<TState>(TState state, string context)
        where TState : class
    {
        Assert.NotNull(KenbaiWasmParityTestSupport.GetWasmJsonTypeInfo(typeof(TState)));

        string nativeJson = SerializeCanonical(state, NativeOptions);
        string wasmJson = SerializeCanonical(state, WasmOptions);

        Assert.True(
            string.Equals(nativeJson, wasmJson, StringComparison.Ordinal),
            $"{context}: native/wasm serialized JSON differs.{Environment.NewLine}native={nativeJson}{Environment.NewLine}wasm={wasmJson}");

        TState nativeRoundTrip = DeserializeState<TState>(nativeJson, NativeOptions, $"{context} native");
        TState wasmRoundTrip = DeserializeState<TState>(nativeJson, WasmOptions, $"{context} wasm");

        string nativeRoundTripJson = SerializeCanonical(nativeRoundTrip, NativeOptions);
        string wasmRoundTripJson = SerializeCanonical(wasmRoundTrip, NativeOptions);

        Assert.True(
            string.Equals(nativeJson, nativeRoundTripJson, StringComparison.Ordinal),
            $"{context}: native roundtrip changed state JSON.");
        Assert.True(
            string.Equals(nativeJson, wasmRoundTripJson, StringComparison.Ordinal),
            $"{context}: wasm roundtrip changed state JSON.");
    }

    private static TState DeserializeState<TState>(string json, JsonSerializerOptions options, string context)
        where TState : class =>
        JsonSerializer.Deserialize<TState>(json, options)
        ?? throw new InvalidOperationException($"{context} deserializer returned null.");

    private static string SerializeCanonical<TState>(TState state, JsonSerializerOptions options)
        where TState : class =>
        KenbaiWasmParityTestSupport.CanonicalizeJson(
            JsonSerializer.Serialize(state, typeof(TState), options));
}
