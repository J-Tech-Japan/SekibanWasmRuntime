using System.IO;

// Public-consumer Aspire AppHost: runs the PUBLIC runtime container image and a
// Postgres event DB. It never uses AddProject<...Host> — only the published
// ghcr.io image, exactly as an external developer would consume it.

const string RuntimeImage = "ghcr.io/j-tech-japan/sekiban-wasm-runtime-host";
const string RuntimeImageTag = "1.0.0-preview.1";
const string ModuleFileName = "public-container-cs-decider.wasm";

var builder = DistributedApplication.CreateBuilder(args);

// Resolve the generated WASM module + manifest (repo-root/artifacts/...).
var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", ".."));
var artifactDir = Path.Combine(repoRoot, "artifacts", "samples", "public-container-cs-decider");
var modulesDir = Path.Combine(artifactDir, "modules");
var configDir = Path.Combine(artifactDir, "config");
var moduleFile = Path.Combine(modulesDir, ModuleFileName);
var manifestFile = Path.Combine(configDir, "sekiban-manifest.json");

// Fail closed with a clear repair command if the mounted inputs are missing.
if (!File.Exists(moduleFile) || !File.Exists(manifestFile))
{
    Console.Error.WriteLine(
        $"[apphost] Missing WASM module or manifest under {artifactDir}.\n" +
        "[apphost] Build them first:\n" +
        "  bash src/samples/Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider/scripts/build-wasm.sh");
    return 1;
}

// External Postgres event DB through Aspire. Naming the database "SekibanDcb"
// makes WithReference inject ConnectionStrings__SekibanDcb into the container.
var postgres = builder.AddPostgres("pg");
var sekibanDb = postgres.AddDatabase("SekibanDcb");

var runtime = builder
    .AddContainer("runtime", RuntimeImage, RuntimeImageTag)
    .WithReference(sekibanDb)
    .WaitFor(sekibanDb)
    .WithBindMount(configDir, "/app/config", isReadOnly: true)
    .WithBindMount(modulesDir, "/app/modules", isReadOnly: true)
    .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:8080")
    .WithEnvironment("SEKIBAN_MANIFEST_PATH", "/app/config/sekiban-manifest.json")
    .WithEnvironment("WASM_MODULE_PATH", $"/app/modules/{ModuleFileName}");

// The smoke script pins a known host port via SAMPLE_RUNTIME_HOST_PORT so it can
// reach the runtime deterministically; otherwise Aspire assigns one.
if (int.TryParse(Environment.GetEnvironmentVariable("SAMPLE_RUNTIME_HOST_PORT"), out var hostPort) && hostPort > 0)
{
    runtime.WithHttpEndpoint(targetPort: 8080, port: hostPort, name: "http", isProxied: false);
}
else
{
    runtime.WithHttpEndpoint(targetPort: 8080, name: "http");
}

runtime.WithExternalHttpEndpoints();

builder.Build().Run();
return 0;
