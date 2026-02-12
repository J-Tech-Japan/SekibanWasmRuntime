using SekibanWasm.Rust.Domain;
using Xunit;

namespace SekibanWasm.Rust.Tests;

public class DomainTypeTests
{
    [Fact]
    public void GetDomainTypes_ShouldReturnNonNull()
    {
        // When
        var domainTypes = DomainType.GetDomainTypes();

        // Then
        Assert.NotNull(domainTypes);
    }

    [Fact]
    public void GetDomainTypes_ShouldRegisterEventTypes()
    {
        // When
        var domainTypes = DomainType.GetDomainTypes();

        // Then
        Assert.NotNull(domainTypes.EventTypes);
    }
}
