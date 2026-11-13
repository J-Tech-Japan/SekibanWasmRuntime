using Microsoft.Extensions.Configuration;
using Orleans.Configuration;
using Sekiban.Dcb.WasmRuntime.Host;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class RuntimeHostOrleansOptionsTests
{
    private const string EnvVar = RuntimeHostOrleansOptions.ActivationTimeoutEnvironmentVariable;

    private static void WithClearedEnvVar(Action testBody)
    {
        var previous = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, null);
            testBody();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, previous);
        }
    }

    private static void WithEnvVar(string value, Action testBody)
    {
        var previous = Environment.GetEnvironmentVariable(EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(EnvVar, value);
            testBody();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, previous);
        }
    }

    [Fact]
    public void Apply_DefaultsCollectionAgeAndActivationTimeoutToFiveMinutes()
    {
        WithClearedEnvVar(() =>
        {
            var configuration = new ConfigurationBuilder().Build();
            var options = new GrainCollectionOptions();

            RuntimeHostOrleansOptions.ApplyGrainCollectionOptions(options, configuration);

            Assert.Equal(TimeSpan.FromMinutes(5), options.CollectionAge);
            Assert.Equal(TimeSpan.FromMinutes(5), options.ActivationTimeout);
        });
    }

    [Fact]
    public void Apply_HonoursActivationTimeoutSecondsFromConfiguration()
    {
        WithClearedEnvVar(() =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [RuntimeHostOrleansOptions.ActivationTimeoutConfigKey] = "90"
                })
                .Build();
            var options = new GrainCollectionOptions();

            RuntimeHostOrleansOptions.ApplyGrainCollectionOptions(options, configuration);

            Assert.Equal(TimeSpan.FromSeconds(90), options.ActivationTimeout);
            Assert.Equal(TimeSpan.FromMinutes(5), options.CollectionAge);
        });
    }

    [Fact]
    public void Apply_HonoursActivationTimeoutSecondsFromEnvironmentVariable()
    {
        WithEnvVar("120", () =>
        {
            var configuration = new ConfigurationBuilder().Build();
            var options = new GrainCollectionOptions();

            RuntimeHostOrleansOptions.ApplyGrainCollectionOptions(options, configuration);

            Assert.Equal(TimeSpan.FromSeconds(120), options.ActivationTimeout);
        });
    }

    [Fact]
    public void Apply_ConfigurationWinsOverEnvironmentVariable()
    {
        WithEnvVar("120", () =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [RuntimeHostOrleansOptions.ActivationTimeoutConfigKey] = "90"
                })
                .Build();
            var options = new GrainCollectionOptions();

            RuntimeHostOrleansOptions.ApplyGrainCollectionOptions(options, configuration);

            Assert.Equal(TimeSpan.FromSeconds(90), options.ActivationTimeout);
        });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public void Apply_NonPositiveOrUnparsedConfigKeepsDefaultAndIgnoresEnvironment(string configValue)
    {
        WithEnvVar("120", () =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [RuntimeHostOrleansOptions.ActivationTimeoutConfigKey] = configValue
                })
                .Build();
            var options = new GrainCollectionOptions();

            RuntimeHostOrleansOptions.ApplyGrainCollectionOptions(options, configuration);

            Assert.Equal(TimeSpan.FromMinutes(5), options.ActivationTimeout);
        });
    }
}
