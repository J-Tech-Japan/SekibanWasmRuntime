using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

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

    [Fact]
    public void GetWasmDomainTypes_ShouldRegisterAotCompatibleDomainMetadata()
    {
        // When
        var domainTypes = DomainType.GetWasmDomainTypes();
        var payload = domainTypes.EventTypes.DeserializeEventPayload(
            nameof(WeatherForecastCreated),
            """
            {
              "forecastId": "forecast-1",
              "location": "Tokyo",
              "temperatureC": 25,
              "summary": "Warm",
              "createdAt": "2026-03-14T00:00:00Z"
            }
            """);

        // Then
        Assert.IsType<WeatherForecastCreated>(payload);
        Assert.Contains(WeatherForecastTag.TagGroupName, domainTypes.TagTypes.GetAllTagGroupNames());
        Assert.Contains(WeatherForecastProjector.ProjectorName, domainTypes.TagProjectorTypes.GetAllProjectorNames());
    }
}
