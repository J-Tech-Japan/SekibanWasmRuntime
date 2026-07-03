using System.IO;

// Aspire AppHost: runs the PUBLIC Sekiban WASM runtime container image and a
// Postgres event DB. The container wiring comes from the
// Sekiban.Dcb.WasmRuntime.Aspire package's AddSekibanWasmRuntime call.

// Defaults to 1.0.0-preview.3 — the verified, recommended public tag: a multi-arch
// (linux/amd64 + linux/arm64) manifest list carrying both libwasmtime.so and the
// WASI preview2 shim, so list-query and Materialized View catch-up work and no
// DOCKER_DEFAULT_PLATFORM=linux/amd64 override is needed on Apple Silicon. Override
// with SAMPLE_RUNTIME_IMAGE_TAG to pin a different published tag.
var runtimeImageTag = Environment.GetEnvironmentVariable("SAMPLE_RUNTIME_IMAGE_TAG") is { Length: > 0 } tagOverride
    ? tagOverride
    : "1.0.0-preview.3";
const string ModuleFileName = "SekibanDcbDecider.wasm";

var builder = DistributedApplication.CreateBuilder(args);

// Resolve the generated WASM module + manifest (solution-root modules/ and
// config/, produced by scripts/build-wasm.sh).
var solutionRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, ".."));
var modulesDir = Path.Combine(solutionRoot, "modules");
var configDir = Path.Combine(solutionRoot, "config");
var moduleFile = Path.Combine(modulesDir, ModuleFileName);
var manifestFile = Path.Combine(configDir, "sekiban-manifest.json");

// Fail closed with a clear repair command if the mounted inputs are missing.
if (!File.Exists(moduleFile) || !File.Exists(manifestFile))
{
    Console.Error.WriteLine(
        $"[apphost] Missing WASM module or manifest under {solutionRoot}.\n" +
        "[apphost] Build them first:\n" +
        "  bash scripts/build-wasm.sh");
    return 1;
}

// External Postgres through Aspire. Naming a database "SekibanDcb" makes
// WithReference inject ConnectionStrings__SekibanDcb (the event store). A second
// database "DcbMaterializedViewPostgres" backs the materialized-view runtime
// (registry + MV state tables) — the runtime host activates the WASM MV executor
// only when the manifest declares materializedViews, the projection mode is
// dual/materialized-view-only, AND this connection string is present.
var postgres = builder.AddPostgres("pg");
var sekibanDb = postgres.AddDatabase("SekibanDcb");
var materializedViewDb = postgres.AddDatabase("DcbMaterializedViewPostgres");

// One call wires the public GHCR image, the read-only /app/config + /app/modules
// bind mounts, the env contract (ASPNETCORE_URLS, SEKIBAN_PROJECTION_MODE=dual,
// SEKIBAN_MANIFEST_PATH, WASM_MODULE_PATH), the Postgres references, and the HTTP
// endpoint. The runtime connects to Postgres lazily and retries, so the package
// deliberately adds no WaitFor gate — that gate can stall a headless run before
// the runtime container is ever created.
var runtimeOptions = new SekibanWasmRuntimeOptions
{
    Tag = runtimeImageTag,
    ConfigDirectory = configDir,
    ModulesDirectory = modulesDir,
    WasmModulePath = $"/app/modules/{ModuleFileName}",
    EventStoreDatabase = sekibanDb,
    MaterializedViewDatabase = materializedViewDb,
};

// The smoke script pins a known host port via SAMPLE_RUNTIME_HOST_PORT so it can
// reach the runtime deterministically; otherwise Aspire assigns one.
if (int.TryParse(Environment.GetEnvironmentVariable("SAMPLE_RUNTIME_HOST_PORT"), out var hostPort) && hostPort > 0)
{
    runtimeOptions.HostPort = hostPort;
}

builder.AddSekibanWasmRuntime("runtime", runtimeOptions);

builder.Build().Run();
return 0;
