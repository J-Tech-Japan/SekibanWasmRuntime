using System.Reflection;
using Aic.Kenbai.EventSource;
using Aic.Kenbai.EventSource.Wasm;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class KenbaiWasmDomainCoverageTests
{
    [Fact]
    public void WasmDomain_ShouldRegisterAllKenbaiTagProjectors()
    {
        HashSet<string> expectedProjectors = typeof(KenbaiDcbDomainType).Assembly
            .GetTypes()
            .Where(IsKenbaiTagProjector)
            .Select(static type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> actualProjectors = KenbaiWasmParityTestSupport.WasmDomainTypes.TagProjectorTypes
            .GetAllProjectorNames()
            .ToHashSet(StringComparer.Ordinal);

        string[] missingProjectors = expectedProjectors
            .Except(actualProjectors, StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingProjectors.Length == 0,
            $"WASM domain is missing tag projector registrations: {string.Join(", ", missingProjectors)}");
    }

    [Fact]
    public void WasmDomain_ShouldRegisterAllKenbaiTagStatePayloads()
    {
        HashSet<string> expectedPayloads = typeof(KenbaiDcbDomainType).Assembly
            .GetTypes()
            .Where(static type =>
                type is { IsAbstract: false, IsInterface: false }
                && typeof(ITagStatePayload).IsAssignableFrom(type))
            .Select(static type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> actualPayloads = GetRegisteredTagStatePayloadNames(
            KenbaiWasmParityTestSupport.WasmDomainTypes.TagStatePayloadTypes);

        string[] missingPayloads = expectedPayloads
            .Except(actualPayloads, StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingPayloads.Length == 0,
            $"WASM domain is missing tag state payload registrations: {string.Join(", ", missingPayloads)}");
    }

    [Fact]
    public void WasmDomain_ShouldRegisterAllKenbaiEventPayloads()
    {
        HashSet<string> expectedEvents = KenbaiWasmParityTestSupport.LoadDistinctEventTypeNamesFromDb()
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> actualEvents = GetRegisteredEventNames(
            KenbaiWasmParityTestSupport.WasmDomainTypes.EventTypes);

        string[] missingEvents = expectedEvents
            .Except(actualEvents, StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missingEvents.Length == 0,
            $"WASM domain is missing event payload registrations: {string.Join(", ", missingEvents)}");
    }

    [Fact]
    public void WasmJsonContext_ShouldProvideTypeInfoForAllKenbaiTagStatePayloads()
    {
        Type[] payloadTypes = typeof(KenbaiDcbDomainType).Assembly
            .GetTypes()
            .Where(static type =>
                type is { IsAbstract: false, IsInterface: false }
                && typeof(ITagStatePayload).IsAssignableFrom(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        List<string> missingTypeInfos = [];
        foreach (Type payloadType in payloadTypes)
        {
            if (AicDomainJsonContext.Default.GetTypeInfo(payloadType) is null)
            {
                missingTypeInfos.Add(payloadType.FullName ?? payloadType.Name);
            }
        }

        Assert.True(
            missingTypeInfos.Count == 0,
            $"AicDomainJsonContext is missing tag state type info: {string.Join(", ", missingTypeInfos)}");
    }

    private static bool IsKenbaiTagProjector(Type type) =>
        type is { IsAbstract: false, IsInterface: false }
        && type.GetInterfaces().Any(static iface =>
            iface.IsGenericType
            && iface.GetGenericTypeDefinition() == typeof(ITagProjector<>));

    private static HashSet<string> GetRegisteredTagStatePayloadNames(ITagStatePayloadTypes payloadTypes)
    {
        FieldInfo field = payloadTypes.GetType().GetField("_deserializers", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not inspect AOT tag state payload registrations.");

        if (field.GetValue(payloadTypes) is not IDictionary<string, object> dictionary)
        {
            if (field.GetValue(payloadTypes) is System.Collections.IDictionary nonGeneric)
            {
                return nonGeneric.Keys
                    .Cast<object>()
                    .Select(static key => key.ToString() ?? string.Empty)
                    .Where(static key => !string.IsNullOrWhiteSpace(key))
                    .ToHashSet(StringComparer.Ordinal);
            }

            throw new InvalidOperationException("Unexpected AOT tag state payload registry shape.");
        }

        return dictionary.Keys.ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> GetRegisteredEventNames(IEventTypes eventTypes)
    {
        FieldInfo field = eventTypes.GetType().GetField("_deserializers", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not inspect AOT event registrations.");

        if (field.GetValue(eventTypes) is not System.Collections.IDictionary dictionary)
        {
            throw new InvalidOperationException("Unexpected AOT event registry shape.");
        }

        return dictionary.Keys
            .Cast<object>()
            .Select(static key => key.ToString() ?? string.Empty)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
    }
}
