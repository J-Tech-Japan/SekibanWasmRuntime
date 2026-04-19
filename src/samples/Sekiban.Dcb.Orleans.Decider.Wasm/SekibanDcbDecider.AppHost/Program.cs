using Projects;
using System.Text.Json;
using System.Text.Json.Nodes;

var benchmarkProfile = Environment.GetEnvironmentVariable("BENCHMARK_PROFILE");
var isStrictBenchmarkProfile = string.Equals(benchmarkProfile, "tagstategrain-memory", StringComparison.OrdinalIgnoreCase);
var projectionMode = (Environment.GetEnvironmentVariable("SEKIBAN_PROJECTION_MODE") ?? "dual").Trim().ToLowerInvariant();
var materializedViewEnabled = projectionMode is "dual" or "materialized-view-only";
var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args,
    DisableDashboard = isStrictBenchmarkProfile,
    EnableResourceLogging = isStrictBenchmarkProfile
});

var postgresServer = builder
    .AddPostgres("dcbOrleansPostgres")
    .WithDbGate();
var wasmPostgres = postgresServer.AddDatabase("SekibanCSharpDb");
// Dedicated database for the Sekiban.Dcb.MaterializedView runtime. The MV runtime
// (MaterializedViewGrain + PostgresMvExecutor + MvCatchUpWorker) runs inside `wasmserver`,
// reads events from the event-store database above, and writes projected rows here. ClientApi
// connects to this database read-only for `/api/mv/*` endpoints and does not perform catch-up.
// In memory-only mode the MV database is skipped so the benchmark captures a pure
// MultiProjection footprint without the overhead of a second Postgres instance.
var mvPostgres = materializedViewEnabled
    ? postgresServer.AddDatabase("SekibanCSharpMvDb")
    : null;

var csharpWasmModulePath = ResolveCSharpWasmModulePath();
var csharpManifestPath = ResolveCSharpManifestPath(csharpWasmModulePath);
var wasmApiPort = ResolveConfiguredPort(5199, "E2E_WASM_PORT");
var wasmServerBuilder = builder
    .AddProject<Sekiban_Dcb_WasmRuntime_Host>("wasmserver")
    .WithEnvironment("SEKIBAN_STORAGE_PROVIDER", "postgres")
    .WithEnvironment("WASM_MODULE_PATH", csharpWasmModulePath)
    .WithEnvironment("SEKIBAN_MANIFEST_PATH", csharpManifestPath)
    .WithEnvironment("SEKIBAN_WASM_CATCHUP_CONCURRENCY", "4")
    .WithEnvironment("SEKIBAN_WASM_MULTIPROJECTION_CATCHUP_BATCH_SIZE", "250")
    .WithEnvironment("SEKIBAN_WASM_AUTO_COMPACTION_INTERVAL_EVENTS", "20000")
    .WithEnvironment("SEKIBAN_WASM_FORCE_COMPACTING_GC_AFTER_COMPACTION", "true")
    .WithEnvironment("SEKIBAN_WASMTIME_STATIC_MEMORY_MAX_MB", "192")
    .WithReference(wasmPostgres, "SekibanDcb")
    .WaitFor(wasmPostgres)
    .WithEnvironment("SEKIBAN_PROJECTION_MODE", projectionMode)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

if (mvPostgres is not null)
{
    // MV runtime (grain + catch-up) lives in the wasm runtime host. Wire the MV Postgres DB so
    // `MaterializedViewGrain` + `PostgresMvExecutor` can initialize schemas, track position, and
    // project into the MV tables.
    wasmServerBuilder = wasmServerBuilder
        .WithReference(mvPostgres, "DcbMaterializedViewPostgres")
        .WaitFor(mvPostgres);
}

wasmServerBuilder = ApplyTagStateDiagnostics(
    wasmServerBuilder,
    defaultRuntimeLabel: "cs-wasm");

if (isStrictBenchmarkProfile)
{
    wasmServerBuilder = wasmServerBuilder
        .WithEnvironment("SEKIBAN_DIRECT_SNAPSHOT_QUERY_ENABLED", "false")
        .WithEnvironment("SEKIBAN_TAG_STATE_FAST_PATH_ENABLED", "false");
}

wasmServerBuilder = wasmServerBuilder
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = wasmApiPort;
        endpoint.TargetPort = wasmApiPort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + wasmApiPort);

var wasmServer = wasmServerBuilder;

var clientApiPort = ResolveConfiguredPort(5198, "E2E_CLIENT_API_PORT");
var clientApiBuilder = builder
    .AddProject<SekibanDcbDecider_ClientApi>("clientapi")
    .WithReference(wasmServer)
    .WaitFor(wasmServer)
    .WithEnvironment("SEKIBAN_PROJECTION_MODE", projectionMode);

if (mvPostgres is not null)
{
    // ClientApi also needs direct read access to the MV database so `/api/mv/*` endpoints can
    // run SELECTs against the projected tables. The MV catch-up itself runs inside the wasm
    // runtime host (wasmserver) via its `MaterializedViewGrain`, not here.
    clientApiBuilder = clientApiBuilder
        .WithReference(mvPostgres, "DcbMaterializedViewPostgres")
        .WaitFor(mvPostgres);
}

clientApiBuilder = clientApiBuilder
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = clientApiPort;
        endpoint.TargetPort = clientApiPort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + clientApiPort)
    .WithEnvironment("WASM_SERVER_URL", "http://127.0.0.1:" + wasmApiPort);

var clientApi = clientApiBuilder;

// Web + WebNext frontends — the Sekiban-dcb template's Blazor + Next.js UI. The C# WASM
// sample does not ship an ApiService (Orleans Identity) project, so AUTH_API_URL points
// at the same ClientApi; the auth endpoints don't exist yet, meaning the Login pages are
// aspirational until /auth/* is added here or an ApiService gets wired in. The rest of
// the pages (meeting rooms, reservations, weather, classrooms, students, enrollments,
// test-data) exercise routes ClientApi already exposes.
var webPort = ResolveConfiguredPort(5180, "E2E_WEB_PORT");
var webFrontend = builder
    .AddProject<SekibanDcbDecider_Web>("webfrontend")
    .WithReference(clientApi.GetEndpoint("http"))
    .WithEnvironment("CLIENT_API_URL", "http://127.0.0.1:" + clientApiPort)
    .WithEnvironment("AUTH_API_URL", "http://127.0.0.1:" + clientApiPort)
    .WaitFor(clientApi)
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = webPort;
        endpoint.TargetPort = webPort;
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
    })
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + webPort);
webFrontend.WithExternalHttpEndpoints();

var webNextPort = ResolveConfiguredPort(3000, "E2E_WEBNEXT_PORT", "WEBNEXT_PORT");
builder
    .AddJavaScriptApp("webnext", "../SekibanDcbDecider.WebNext")
    .WithHttpEndpoint(port: webNextPort, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("NODE_ENV", "development")
    .WithEnvironment("API_BASE_URL", "http://127.0.0.1:" + clientApiPort)
    .WithEnvironment("CLIENT_API_BASE_URL", "http://127.0.0.1:" + clientApiPort)
    .WaitFor(clientApi);

builder.Build().Run();

static int ResolveConfiguredPort(int defaultPort, params string[] envNames)
{
    foreach (string envName in envNames)
    {
        string? value = Environment.GetEnvironmentVariable(envName);
        if (int.TryParse(value, out int port))
        {
            return port;
        }
    }

    return defaultPort;
}

static string ResolveCSharpWasmModulePath()
{
    string? envPath = Environment.GetEnvironmentVariable("CS_WASM_MODULE_PATH")
        ?? Environment.GetEnvironmentVariable("WASM_MODULE_PATH");
    if (!string.IsNullOrWhiteSpace(envPath))
    {
        return Path.IsPathRooted(envPath)
            ? envPath
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), envPath));
    }

    string[] candidates =
    [
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "modules",
            "sekiban-dcb-decider.wasm")),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "..",
            "..",
            "..",
            "artifacts",
            "sekiban-dcb-decider-wasm",
            "SekibanDcbDecider.Wasm.wasm")),
        Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "SekibanDcbDecider.Wasm",
            "bin",
            "Release",
            "net10.0",
            "wasi-wasm",
            "native",
            "SekibanDcbDecider.Wasm.wasm"))
    ];

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new InvalidOperationException(
        "C# WASM module not found. Set CS_WASM_MODULE_PATH or build SekibanDcbDecider.Wasm first.");
}

static IResourceBuilder<ProjectResource> ApplyTagStateDiagnostics(
    IResourceBuilder<ProjectResource> resource,
    string defaultRuntimeLabel)
{
    string? enabled = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_ENABLED");
    if (string.IsNullOrWhiteSpace(enabled))
    {
        return resource;
    }

    resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_ENABLED", enabled);

    string? slowMs = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_SLOW_MS");
    if (!string.IsNullOrWhiteSpace(slowMs))
    {
        resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_SLOW_MS", slowMs);
    }

    string? summaryEvery = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_SUMMARY_EVERY");
    if (!string.IsNullOrWhiteSpace(summaryEvery))
    {
        resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_SUMMARY_EVERY", summaryEvery);
    }

    string? projectors = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_PROJECTORS");
    if (!string.IsNullOrWhiteSpace(projectors))
    {
        resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_PROJECTORS", projectors);
    }

    string? outputPath = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_FILE");
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        resource = resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_FILE", outputPath);
    }

    string runtimeLabel = Environment.GetEnvironmentVariable("SEKIBAN_TAG_STATE_DIAGNOSTICS_RUNTIME_LABEL")
        ?? defaultRuntimeLabel;
    return resource.WithEnvironment("SEKIBAN_TAG_STATE_DIAGNOSTICS_RUNTIME_LABEL", runtimeLabel);
}

static string ResolveCSharpManifestPath(string wasmModulePath)
{
    string? envPath = Environment.GetEnvironmentVariable("CS_WASM_MANIFEST_PATH")
        ?? Environment.GetEnvironmentVariable("SEKIBAN_MANIFEST_PATH");
    if (!string.IsNullOrWhiteSpace(envPath))
    {
        string resolved = Path.IsPathRooted(envPath)
            ? envPath
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), envPath));
        if (File.Exists(resolved))
        {
            return resolved;
        }
    }

    string candidate = Path.GetFullPath(Path.Combine(
        Directory.GetCurrentDirectory(),
        "..",
        "modules",
        "sekiban-runtime-manifest.json"));
    if (!File.Exists(candidate))
    {
        throw new InvalidOperationException(
            "C# WASM manifest not found. Set CS_WASM_MANIFEST_PATH or provide modules/sekiban-runtime-manifest.json.");
    }

    JsonNode root = JsonNode.Parse(File.ReadAllText(candidate))
        ?? throw new InvalidOperationException($"Failed to parse manifest template at '{candidate}'.");
    root["defaultModulePath"] = wasmModulePath;
    if (root["projectors"] is JsonArray projectors)
    {
        foreach (JsonNode? projector in projectors)
        {
            if (projector is JsonObject projectorObject)
            {
                projectorObject["modulePath"] = wasmModulePath;
            }
        }
    }

    string generatedDirectory = Path.Combine(Directory.GetCurrentDirectory(), ".generated");
    Directory.CreateDirectory(generatedDirectory);
    string generatedPath = Path.Combine(generatedDirectory, "sekiban-runtime-manifest.generated.json");
    File.WriteAllText(generatedPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return generatedPath;
}
