using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var storage = builder
    .AddAzureStorage("azurestorage")
    .RunAsEmulator();
var clusteringTable = storage.AddTables("SekibanWasmClusteringTable");
var grainStorage = storage.AddBlobs("SekibanWasmGrainState");
var queue = storage.AddQueues("SekibanWasmQueue");

var postgres = builder
    .AddPostgres("sekibanWasmPostgres")
    .AddDatabase("SekibanWasmDb");

var orleans = builder
    .AddOrleans("default")
    .WithClustering(clusteringTable)
    .WithGrainStorage("Default", grainStorage)
    .WithStreaming(queue);

// E2E support: allow scripts to pin the ApiService external http port for stable smoke tests.
var e2eApiPort = int.TryParse(Environment.GetEnvironmentVariable("E2E_API_PORT"), out var parsedApiPort)
    ? parsedApiPort
    : 0;

var apiService = builder
    .AddProject<SekibanWasm_ApiService>("apiservice")
    .WithReference(postgres)
    .WithReference(orleans)
    .WaitFor(postgres);

if (e2eApiPort is > 0 and < 65536)
{
    // For smoke tests, bind the ApiService directly to a known free port.
    // Avoid DCP external endpoint binding here: it can add another listener on the same port.
    apiService = apiService.WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:" + e2eApiPort.ToString());
}

builder.Build().Run();
