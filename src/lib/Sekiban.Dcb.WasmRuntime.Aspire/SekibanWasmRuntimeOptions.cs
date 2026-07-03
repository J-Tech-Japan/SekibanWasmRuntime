using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Options for <see cref="SekibanWasmRuntimeBuilderExtensions.AddSekibanWasmRuntime"/>.
/// The surface is intentionally minimal and mirrors the public runtime container
/// contract (docker/sekiban-wasm-runtime/README.md): image/tag, wasm module and
/// manifest locations (host bind mounts plus in-container paths), Postgres
/// references, environment overrides, endpoint, and health check.
/// </summary>
public sealed class SekibanWasmRuntimeOptions
{
    /// <summary>Runtime container image. Defaults to the public GHCR image.</summary>
    public string Image { get; set; } = "ghcr.io/j-tech-japan/sekiban-wasm-runtime-host";

    /// <summary>Runtime image tag. Defaults to the verified public preview tag.</summary>
    public string Tag { get; set; } = "1.0.0-preview.3";

    /// <summary>
    /// Host directory bind-mounted read-only to <c>/app/config</c> (runtime manifest).
    /// Leave null when the manifest is provided some other way (e.g. a derived image).
    /// </summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>
    /// Host directory bind-mounted read-only to <c>/app/modules</c> (wasm modules).
    /// Leave null when the module is provided some other way (e.g. a derived image).
    /// </summary>
    public string? ModulesDirectory { get; set; }

    /// <summary>In-container path of the runtime manifest (<c>SEKIBAN_MANIFEST_PATH</c>).</summary>
    public string ManifestPath { get; set; } = "/app/config/sekiban-manifest.json";

    /// <summary>
    /// In-container path of the default projector module (<c>WASM_MODULE_PATH</c>),
    /// e.g. <c>/app/modules/my-projector.wasm</c>. Required.
    /// </summary>
    public string? WasmModulePath { get; set; }

    /// <summary>Projection mode (<c>SEKIBAN_PROJECTION_MODE</c>): dual, memory-only, or materialized-view-only.</summary>
    public string ProjectionMode { get; set; } = "dual";

    /// <summary>
    /// Event-store database. Name the Aspire database resource <c>SekibanDcb</c> so the
    /// reference injects <c>ConnectionStrings__SekibanDcb</c>, which the runtime expects.
    /// </summary>
    public IResourceBuilder<IResourceWithConnectionString>? EventStoreDatabase { get; set; }

    /// <summary>
    /// Materialized-view database. Name the Aspire database resource
    /// <c>DcbMaterializedViewPostgres</c>; the MV runtime activates only when the manifest
    /// declares materialized views, the projection mode allows them, and this reference exists.
    /// </summary>
    public IResourceBuilder<IResourceWithConnectionString>? MaterializedViewDatabase { get; set; }

    /// <summary>
    /// Extra or overriding environment variables, applied after the standard contract
    /// (<c>ASPNETCORE_URLS</c>, <c>SEKIBAN_PROJECTION_MODE</c>, <c>SEKIBAN_MANIFEST_PATH</c>,
    /// <c>WASM_MODULE_PATH</c>) so entries here win.
    /// </summary>
    public IDictionary<string, string> EnvironmentVariables { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Name of the HTTP endpoint resource. Defaults to <c>http</c>.</summary>
    public string EndpointName { get; set; } = "http";

    /// <summary>Container port the runtime listens on. Defaults to 8080.</summary>
    public int TargetPort { get; set; } = 8080;

    /// <summary>
    /// Fixed host port for the endpoint (published unproxied, so scripts can reach the
    /// runtime deterministically). Leave null to let Aspire assign one.
    /// </summary>
    public int? HostPort { get; set; }

    /// <summary>Mark the HTTP endpoint external (dashboard link). Defaults to true.</summary>
    public bool ExternalHttpEndpoints { get; set; } = true;

    /// <summary>
    /// HTTP health-check path (e.g. <c>/ready</c> for the strict readiness probe, or
    /// <c>/health</c> for liveness). Null (default) adds no health check — note that
    /// gating other resources on runtime readiness can stall headless runs; the runtime
    /// also tolerates Postgres arriving after it starts.
    /// </summary>
    public string? HealthCheckPath { get; set; }
}
