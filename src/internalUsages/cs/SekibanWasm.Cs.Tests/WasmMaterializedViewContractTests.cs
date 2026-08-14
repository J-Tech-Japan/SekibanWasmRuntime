using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.WasmRuntime.Host;
using Sekiban.Dcb.WasmRuntime.Host.MaterializedView;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class WasmMaterializedViewContractTests
{
    [Fact]
    public void MetadataValidation_IsOrderInsensitive_ButRejectsDuplicateOrMissingViews()
    {
        var declarations = new[]
        {
            Declaration("WeatherForecast", 1, "weather_forecast"),
            Declaration("ClassRoomEnrollment", 1, "classrooms", "students", "enrollments")
        };
        var metadata = declarations
            .Reverse()
            .Select(declaration => new WasmMvMetadataDto
            {
                ViewName = declaration.ViewName,
                ViewVersion = declaration.ViewVersion,
                AbiVersion = WasmMvContract.AbiVersion,
                Capabilities = [WasmMvContract.QueryRowsCapability],
                LogicalTables = declaration.LogicalTables.ToList()
            })
            .ToList();

        WasmMaterializedViewContractValidator.ValidateMetadata(declarations, metadata);

        Assert.Throws<WasmMaterializedViewContractException>(() =>
            WasmMaterializedViewContractValidator.ValidateMetadata(declarations, metadata[..1]));

        metadata[0].LogicalTables.Add("duplicate");
        Assert.Throws<WasmMaterializedViewContractException>(() =>
            WasmMaterializedViewContractValidator.ValidateMetadata(declarations, metadata));
    }

    [Fact]
    public void VerifyOnlySchemaContract_IsTruthfulForRegisteredTables()
    {
        var metadata = new WasmMvMetadataDto
        {
            ViewName = "WeatherForecast",
            ViewVersion = 1,
            AbiVersion = WasmMvContract.AbiVersion,
            Capabilities = [WasmMvContract.QueryRowsCapability],
            LogicalTables = ["weather_forecast"],
            Schema =
            [
                new WasmMvSchemaTableDto
                {
                    LogicalTable = "weather_forecast",
                    Columns =
                    [
                        new() { Name = "forecast_id", TypeFamily = WasmMvSchemaTypeFamily.String, IsNullable = false },
                        new() { Name = "created_at", TypeFamily = WasmMvSchemaTypeFamily.DateTime, IsNullable = true }
                    ],
                    PrimaryKeyColumns = ["forecast_id"]
                }
            ]
        };
        var host = new WasmMvApplyHost(
            "WeatherForecast",
            1,
            ["weather_forecast"],
            new NoopWasmExecutor(),
            "tenant-a",
            metadata);
        var bindings = new MvTableBindings("WeatherForecast", 1, new MvOptions());

        var contract = host.GetSchemaContract(bindings);

        Assert.NotNull(contract);
        Assert.Single(contract!.Tables);
        Assert.Equal("weather_forecast", contract.Tables[0].LogicalTable);
        Assert.Equal("forecast_id", contract.Tables[0].PrimaryKeyColumns.Single());
        Assert.True(MvSchemaRequirements.ValidateContract(bindings.Tables, contract.Tables).IsCompatible);
    }

    [Fact]
    public async Task QueryPolicy_RejectsUnsafeScopeCommentsAndMissingParametersBeforePortIo()
    {
        var context = new WasmMvQueryCallbackContext(
            "tenant-a",
            "WeatherForecast",
            1,
            [new MvTable("weather_forecast", "sekiban_mv_WeatherForecast_v1_weather_forecast", "WeatherForecast", 1)]);
        var policy = new WasmMvSqlStatementPolicy();

        var allowed = await policy.EvaluateAsync(new MvSqlStatementContext(
            context.ServiceId,
            context.ViewName,
            context.ViewVersion,
            MvSqlStatementPhase.Apply,
            context.Tables,
            "SELECT forecast_id FROM sekiban_mv_WeatherForecast_v1_weather_forecast WHERE forecast_id = @Id",
            [new MvParam("Id", MvParamKind.String, "\"one\"")])
        {
            Origin = MvSqlStatementOrigin.ProjectorQuery
        });
        Assert.True(allowed.IsAllowed);

        foreach (var sql in new[]
                 {
                     "SELECT forecast_id FROM other_view",
                     "SELECT forecast_id FROM sekiban_mv_WeatherForecast_v1_weather_forecast -- escape",
                     "SELECT forecast_id FROM sekiban_mv_WeatherForecast_v1_weather_forecast; SELECT 1",
                     "UPDATE sekiban_mv_WeatherForecast_v1_weather_forecast SET location = 'x'"
                 })
        {
            var decision = await policy.EvaluateAsync(new MvSqlStatementContext(
                context.ServiceId,
                context.ViewName,
                context.ViewVersion,
                MvSqlStatementPhase.Apply,
                context.Tables,
                sql,
                [])
            {
                Origin = MvSqlStatementOrigin.ProjectorQuery
            });
            Assert.False(decision.IsAllowed, sql);
        }

        Assert.Throws<WasmMvQueryPolicyException>(() => WasmMvSqlStatementPolicy.ValidateQuery(
            context,
            "SELECT forecast_id FROM sekiban_mv_WeatherForecast_v1_weather_forecast WHERE forecast_id = @Id",
            [],
            100));
        Assert.Contains("LIMIT 100", WasmMvSqlStatementPolicy.EnsureBoundedQuery(
            "SELECT forecast_id FROM sekiban_mv_WeatherForecast_v1_weather_forecast",
            100));
    }

    [Fact]
    public void BuiltCSharpModule_UsesOneValidatedWasmtimeByteSnapshot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var modulePath = FindBuiltPublicModule(repositoryRoot);
        if (modulePath is null)
        {
            // The ordinary unit-test developer loop does not build NativeAOT WASM first; CI
            // does, and the dedicated packet gate executes this assertion against that artifact.
            return;
        }

        var digest = WasmMaterializedViewContractValidator.ComputeEffectiveModuleSha256(modulePath);
        var result = WasmMaterializedViewContractValidator.Validate(
            modulePath,
            [new SekibanRuntimeMaterializedView
            {
                ViewName = "WeatherForecast",
                ViewVersion = 1,
                LogicalTables = ["weather_forecast"],
                ModuleSha256 = digest,
                AbiVersion = WasmMvContract.AbiVersion,
                Capabilities = [WasmMvContract.QueryRowsCapability]
            }]);

        Assert.Equal(digest, result.ModuleSha256);
        Assert.NotEmpty(result.ModuleBytes);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(result.ModuleBytes)).ToLowerInvariant(),
            result.InstantiatedModuleSha256);
        Assert.Contains(result.Metadata, metadata => metadata.Schema.Count == 1);
    }

    [Fact]
    public async Task PathSwapAfterValidation_DoesNotChangeExecutedWasmtimeBytes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = FindBuiltPublicModule(repositoryRoot);
        if (sourcePath is null)
        {
            return;
        }

        var swappedPath = Path.Combine(
            Path.GetTempPath(),
            $"swr-g079-{Guid.NewGuid():N}.wasm");
        try
        {
            File.Copy(sourcePath, swappedPath);
            var digest = WasmMaterializedViewContractValidator.ComputeEffectiveModuleSha256(swappedPath);
            var result = WasmMaterializedViewContractValidator.Validate(
                swappedPath,
                [new SekibanRuntimeMaterializedView
                {
                    ViewName = "WeatherForecast",
                    ViewVersion = 1,
                    LogicalTables = ["weather_forecast"],
                    ModuleSha256 = digest,
                    AbiVersion = WasmMvContract.AbiVersion,
                    Capabilities = [WasmMvContract.QueryRowsCapability]
                }]);

            // Replace the validated path with an unusable artifact. The executor must still
            // instantiate the immutable bytes returned by validation, rather than reopening it.
            File.WriteAllBytes(swappedPath, [0]);
            Assert.NotEqual(result.ModuleSha256,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(swappedPath))).ToLowerInvariant());

            using var runtime = new WasmtimeRuntime(new WasmtimeHostOptions());
            using var executor = new WasmtimeMaterializedViewExecutor(
                new WasmMaterializedViewRuntimeOptions
                {
                    ModulePath = swappedPath,
                    ValidatedModule = result
                },
                runtime,
                new WasmtimeModuleCache(runtime),
                NullLogger<WasmtimeMaterializedViewExecutor>.Instance);

            var metadata = await executor.GetMetadataAsync();
            Assert.Contains(metadata, item => item.ViewName == "WeatherForecast" && item.ViewVersion == 1);
        }
        finally
        {
            try { File.Delete(swappedPath); } catch { }
        }
    }

    private static SekibanRuntimeMaterializedView Declaration(
        string viewName,
        int viewVersion,
        params string[] logicalTables) =>
        new()
        {
            ViewName = viewName,
            ViewVersion = viewVersion,
            LogicalTables = logicalTables.ToList(),
            ModuleSha256 = new string('a', 64),
            AbiVersion = WasmMvContract.AbiVersion,
            Capabilities = [WasmMvContract.QueryRowsCapability]
        };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string? FindBuiltPublicModule(string repositoryRoot) => new[]
    {
        Path.Combine(
            repositoryRoot,
            "artifacts",
            "samples",
            "public-container-cs-decider",
            "modules",
            "public-container-cs-decider.wasm"),
        Path.Combine(
            repositoryRoot,
            "src",
            "samples",
            "Sekiban.Dcb.WasmRuntime.PublicContainer.CsDecider",
            "Wasm",
            "bin",
            "Release",
            "net10.0",
            "wasi-wasm",
            "native",
            "PublicContainerCsDecider.Wasm.wasm")
    }.FirstOrDefault(File.Exists);

    private sealed class NoopWasmExecutor : IWasmMaterializedViewExecutor
    {
        public Task<IReadOnlyList<WasmMvMetadataDto>> GetMetadataAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WasmMvMetadataDto>>([]);

        public Task<IReadOnlyList<WasmMvSqlStatementDto>> InitializeAsync(
            string viewName,
            int viewVersion,
            WasmMvTableBindingsDto tableBindings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WasmMvSqlStatementDto>>([]);

        public Task<IReadOnlyList<WasmMvSqlStatementDto>> ApplyEventAsync(
            string viewName,
            int viewVersion,
            WasmMvTableBindingsDto tableBindings,
            WasmMvSerializableEventDto serializableEvent,
            IMvApplyQueryPort queryPort,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WasmMvSqlStatementDto>>([]);
    }
}
