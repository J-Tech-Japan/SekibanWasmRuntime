using Sekiban.Benchmark.Cli;

var config = ParseArgs(args);
if (config is null) return 1;

var runner = new BenchmarkRunner(
    config.BaseUrl, config.ModeLabel, config.TotalEvents,
    config.Concurrency, config.Output, config.SkipSetup);
await runner.RunAsync();
return 0;

static BenchmarkConfig? ParseArgs(string[] args)
{
    var config = new BenchmarkConfig();
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--base-url" when i + 1 < args.Length:
                config.BaseUrl = args[++i];
                break;
            case "--mode-label" when i + 1 < args.Length:
                config.ModeLabel = args[++i];
                break;
            case "--total-events" when i + 1 < args.Length:
                config.TotalEvents = int.Parse(args[++i]);
                break;
            case "--concurrency" when i + 1 < args.Length:
                config.Concurrency = int.Parse(args[++i]);
                break;
            case "--output" when i + 1 < args.Length:
                config.Output = args[++i];
                break;
            case "--skip-setup":
                config.SkipSetup = true;
                break;
            case "--help" or "-h":
                PrintUsage();
                return null;
        }
    }

    if (string.IsNullOrEmpty(config.BaseUrl) || string.IsNullOrEmpty(config.ModeLabel))
    {
        Console.Error.WriteLine("Error: --base-url and --mode-label are required.");
        PrintUsage();
        return null;
    }

    return config;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Sekiban Event Sourcing Benchmark CLI

        Usage:
          dotnet run -- --base-url <url> --mode-label <label> [options]

        Required:
          --base-url <url>        Target API base URL (e.g. http://localhost:5198)
          --mode-label <label>    Runtime mode: native, cs-wasm, rs-wasm

        Options:
          --total-events <n>      Target event count (default: 300000)
          --concurrency <n>       Parallel HTTP clients (default: 8)
          --output <path>         Output JSON file path
          --skip-setup            Skip room creation phase
          --help, -h              Show this help
        """);
}

file sealed class BenchmarkConfig
{
    public string BaseUrl { get; set; } = "";
    public string ModeLabel { get; set; } = "";
    public int TotalEvents { get; set; } = 300_000;
    public int Concurrency { get; set; } = 8;
    public string? Output { get; set; }
    public bool SkipSetup { get; set; }
}
