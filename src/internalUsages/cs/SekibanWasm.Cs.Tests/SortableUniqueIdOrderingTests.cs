using Sekiban.Dcb.Common;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class SortableUniqueIdOrderingTests
{
    [Fact]
    public void PublishedGeneratorProducesStrictlyIncreasingOrdinalTokens()
    {
        var ids = Enumerable.Range(0, 256)
            .Select(_ => SortableUniqueId.GenerateNew())
            .ToArray();

        for (var index = 1; index < ids.Length; index++)
        {
            Assert.True(
                string.CompareOrdinal(ids[index - 1], ids[index]) < 0,
                $"Expected {ids[index - 1]} to sort before {ids[index]}.");
            Assert.True(
                new SortableUniqueId(ids[index - 1]).IsEarlierThan(new SortableUniqueId(ids[index])));
        }
    }
}
