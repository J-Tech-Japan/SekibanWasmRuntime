using System.Text.Json;
using Aic.Kenbai.EventSource;
using Aic.Kenbai.EventSource.Projections.HokenNendoShosais;
using Aic.Kenbai.EventSource.Projections.Kanyushyas;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class KenbaiWasmSerializerParityTests
{
    private static readonly JsonSerializerOptions SerializerOptions =
        KenbaiDcbDomainType.GetDomainTypes().JsonSerializerOptions;

    [Theory]
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
    [InlineData(100, 50)]
    [InlineData(300, 150)]
    [InlineData(1000, 500)]
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

    private static void AssertProjectionParity<TProjection>(
        IReadOnlyList<KenbaiWasmParityTestSupport.EventRow> rows,
        TProjection nativeState,
        string wasmStateJson)
    {
        string nativeJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            JsonSerializer.Serialize(nativeState, SerializerOptions));
        string wasmJson = KenbaiWasmParityTestSupport.CanonicalizeJson(wasmStateJson);

        Assert.True(
            string.Equals(nativeJson, wasmJson, StringComparison.Ordinal),
            $"Projection parity mismatch after {rows.Count} events.{Environment.NewLine}" +
            $"First={rows.First().SortableUniqueId} Last={rows.Last().SortableUniqueId}{Environment.NewLine}" +
            $"Native={nativeJson}{Environment.NewLine}" +
            $"Wasm={wasmJson}");
    }
}
