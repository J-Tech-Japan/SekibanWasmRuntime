using Microsoft.Extensions.Configuration;
using Orleans.Configuration;

namespace Sekiban.Dcb.WasmRuntime.Host;

internal static class RuntimeHostOrleansOptions
{
    internal const string ActivationTimeoutConfigKey = "Sekiban:Orleans:ActivationTimeoutSeconds";
    internal const string ActivationTimeoutEnvironmentVariable =
        "SEKIBAN_WASM_ORLEANS_ACTIVATION_TIMEOUT_SECONDS";
    internal const int DefaultActivationTimeoutSeconds = 300;

    internal static TimeSpan ResolveActivationTimeout(IConfiguration configuration)
    {
        var candidate = configuration[ActivationTimeoutConfigKey]
            ?? Environment.GetEnvironmentVariable(ActivationTimeoutEnvironmentVariable);
        if (int.TryParse(candidate, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(DefaultActivationTimeoutSeconds);
    }

    internal static void ApplyGrainCollectionOptions(
        GrainCollectionOptions options,
        IConfiguration configuration)
    {
        options.CollectionAge = TimeSpan.FromMinutes(5);
        options.ActivationTimeout = ResolveActivationTimeout(configuration);
    }
}
