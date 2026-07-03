using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Aspire AppHost extension that wires the public Sekiban WASM Runtime container
/// (<c>ghcr.io/j-tech-japan/sekiban-wasm-runtime-host</c>) in one call.
/// </summary>
public static class SekibanWasmRuntimeBuilderExtensions
{
    /// <summary>
    /// Adds the Sekiban WASM Runtime container to the distributed application:
    /// public GHCR image, read-only bind mounts for the runtime manifest and wasm
    /// modules, Postgres connection references, the runtime environment contract,
    /// an HTTP endpoint, and an optional health check.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name (e.g. <c>runtime</c>).</param>
    /// <param name="options">Runtime wiring options; <see cref="SekibanWasmRuntimeOptions.WasmModulePath"/> is required.</param>
    public static IResourceBuilder<ContainerResource> AddSekibanWasmRuntime(
        this IDistributedApplicationBuilder builder,
        string name,
        SekibanWasmRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.WasmModulePath))
        {
            throw new ArgumentException(
                "SekibanWasmRuntimeOptions.WasmModulePath must be set to the in-container path " +
                "of the projector module, e.g. /app/modules/my-projector.wasm.",
                nameof(options));
        }

        var runtime = builder.AddContainer(name, options.Image, options.Tag);

        if (options.EventStoreDatabase is not null)
        {
            runtime = runtime.WithReference(options.EventStoreDatabase);
        }

        if (options.MaterializedViewDatabase is not null)
        {
            runtime = runtime.WithReference(options.MaterializedViewDatabase);
        }

        if (!string.IsNullOrEmpty(options.ConfigDirectory))
        {
            runtime = runtime.WithBindMount(options.ConfigDirectory, "/app/config", isReadOnly: true);
        }

        if (!string.IsNullOrEmpty(options.ModulesDirectory))
        {
            runtime = runtime.WithBindMount(options.ModulesDirectory, "/app/modules", isReadOnly: true);
        }

        // Concatenate outside the call: WithEnvironment's interpolated-string overload
        // binds to ReferenceExpression, which only accepts resource value providers.
        var aspNetCoreUrls = "http://0.0.0.0:" + options.TargetPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        runtime = runtime
            .WithEnvironment("ASPNETCORE_URLS", aspNetCoreUrls)
            .WithEnvironment("SEKIBAN_PROJECTION_MODE", options.ProjectionMode)
            .WithEnvironment("SEKIBAN_MANIFEST_PATH", options.ManifestPath)
            .WithEnvironment("WASM_MODULE_PATH", options.WasmModulePath);

        // Overrides win over the standard contract above.
        foreach (var (key, value) in options.EnvironmentVariables)
        {
            runtime = runtime.WithEnvironment(key, value);
        }

        // A fixed host port is published unproxied so scripts can reach the runtime
        // deterministically (mirrors the public-container samples).
        runtime = options.HostPort is int hostPort and > 0
            ? runtime.WithHttpEndpoint(targetPort: options.TargetPort, port: hostPort, name: options.EndpointName, isProxied: false)
            : runtime.WithHttpEndpoint(targetPort: options.TargetPort, name: options.EndpointName);

        if (options.ExternalHttpEndpoints)
        {
            runtime = runtime.WithExternalHttpEndpoints();
        }

        if (!string.IsNullOrEmpty(options.HealthCheckPath))
        {
            runtime = runtime.WithHttpHealthCheck(path: options.HealthCheckPath, endpointName: options.EndpointName);
        }

        return runtime;
    }
}
