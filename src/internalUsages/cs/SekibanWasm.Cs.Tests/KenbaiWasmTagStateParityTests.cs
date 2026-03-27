using System.Text;
using Aic.Kenbai.EventSource.KanyushaNos;
using Aic.Kenbai.ImmutableModels.Events.KanyushaNos;
using Sekiban.Dcb;
using Sekiban.Dcb.Tags;
using Xunit;

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

        string wasmRuntimeJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
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

        string wasmRuntimeJson = KenbaiWasmParityTestSupport.CanonicalizeJson(
            KenbaiWasmParityTestSupport.ReplayProjectionWasmWithRestore(
                nameof(KanyushaNumberKanriTagProjector),
                rows,
                restoreAfterCount));

        Assert.Equal(nativeJson, wasmRuntimeJson);
    }

    private static string SerializeCanonicalTagState(DcbDomainTypes domainTypes, ITagStatePayload state)
    {
        byte[] bytes = domainTypes.TagStatePayloadTypes.SerializePayload(state).GetValue();
        string json = Encoding.UTF8.GetString(bytes);
        return KenbaiWasmParityTestSupport.CanonicalizeJson(json);
    }
}
