using System.IO;

const string RuntimeImage = "ghcr.io/j-tech-japan/sekiban-wasm-runtime-host";
var runtimeImageTag = Environment.GetEnvironmentVariable("SAMPLE_RUNTIME_IMAGE_TAG") is { Length: > 0 } tagOverride
    ? tagOverride
    : "1.0.0-preview.3";
const string ModuleFileName = "go-module-go-decider.wasm";

var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", ".."));
var artifactDir = Path.Combine(repoRoot, "artifacts", "samples", "go-module-go-decider");
var modulesDir = Path.Combine(artifactDir, "modules");
var configDir = Path.Combine(artifactDir, "config");
var moduleFile = Path.Combine(modulesDir, ModuleFileName);
var manifestFile = Path.Combine(configDir, "sekiban-manifest.json");

if (!File.Exists(moduleFile) || !File.Exists(manifestFile))
{
    Console.Error.WriteLine(
        $"[apphost] Missing Go WASM module or manifest under {artifactDir}.\n" +
        "[apphost] Build them first:\n" +
        "  bash src/samples/Sekiban.Dcb.WasmRuntime.GoModule.GoDecider/scripts/build-wasm.sh");
    return 1;
}

var postgres = builder.AddPostgres("pg");
var sekibanDb = postgres.AddDatabase("SekibanDcb");
var materializedViewDb = postgres.AddDatabase("DcbMaterializedViewPostgres");

var runtime = builder
    .AddContainer("runtime", RuntimeImage, runtimeImageTag)
    .WithReference(sekibanDb)
    .WithReference(materializedViewDb)
    .WithBindMount(configDir, "/app/config", isReadOnly: true)
    .WithBindMount(modulesDir, "/app/modules", isReadOnly: true)
    .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:8080")
    .WithEnvironment("SEKIBAN_PROJECTION_MODE", "dual")
    .WithEnvironment("SEKIBAN_MANIFEST_PATH", "/app/config/sekiban-manifest.json")
    .WithEnvironment("WASM_MODULE_PATH", $"/app/modules/{ModuleFileName}")
    // TinyGo/Go modules require the unpooled instance mode (see
    // docker/sekiban-wasm-runtime/README.md and the Decider.Wasm.Go sample).
    .WithEnvironment("SEKIBAN_WASM_POOL_SIZE", "0");

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
