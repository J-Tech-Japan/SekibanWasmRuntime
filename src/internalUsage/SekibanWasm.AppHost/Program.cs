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

var apiService = builder
    .AddProject<SekibanWasm_ApiService>("apiservice")
    .WithReference(postgres)
    .WithReference(orleans)
    .WaitFor(postgres);

builder.Build().Run();
