using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
/// Startup gate for the materialized-view WASM boundary. It reads the module once, hashes those
/// exact bytes, instantiates those exact bytes in Wasmtime, and only then accepts the metadata
/// used to register the MV workers. A later path replacement therefore cannot make the host
/// validate one module and execute another.
/// </summary>
public static class WasmMaterializedViewContractValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WasmMaterializedViewValidationResult Validate(
        string modulePath,
        IReadOnlyList<SekibanRuntimeMaterializedView> declarations,
        ulong? staticMemoryMaximumSizeBytes = 64UL * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentNullException.ThrowIfNull(declarations);
        if (declarations.Count == 0)
        {
            throw new WasmMaterializedViewContractException(
                "Materialized-view validation requires at least one manifest declaration.");
        }

        var expected = ValidateDeclarations(modulePath, declarations);
        if (!File.Exists(modulePath))
        {
            throw new WasmMaterializedViewContractException(
                $"Materialized-view module '{modulePath}' is missing.");
        }

        // This is deliberately one read. Components are reduced from this immutable source
        // snapshot, and the resulting core bytes are the bytes passed to Module.FromBytes.
        var moduleBytes = File.ReadAllBytes(modulePath);
        using var runtime = new WasmtimeRuntime(new WasmtimeHostOptions
        {
            StaticMemoryMaximumSizeBytes = staticMemoryMaximumSizeBytes
        });
        var effectiveModuleBytes = new WasmtimeModuleCache(runtime)
            .ReadEffectiveModuleBytes(modulePath, moduleBytes);
        var actualDigest = Convert.ToHexString(SHA256.HashData(effectiveModuleBytes)).ToLowerInvariant();
        if (!expected.All(declaration =>
                string.Equals(declaration.ModuleSha256, actualDigest, StringComparison.OrdinalIgnoreCase)))
        {
            throw new WasmMaterializedViewContractException(
                $"Materialized-view module digest does not match the declared SHA-256 for '{modulePath}'.");
        }

        List<WasmMvMetadataDto> metadata;
        using (var module = Module.FromBytes(runtime.Engine, Path.GetFileNameWithoutExtension(modulePath), effectiveModuleBytes))
        using (var store = new Store(runtime.Engine))
        {
            store.SetWasiConfiguration(new WasiConfiguration());
            var linker = runtime.CreateLinker();
            linker.Define(
                "env",
                "mv_host_query_rows",
                Function.FromCallback<int, int, int, int, int, long>(
                    store,
                    (_, _, _, _, _) => 0L));

            var instance = linker.Instantiate(store, module);
            var memory = instance.GetMemory("memory")
                ?? throw new WasmMaterializedViewContractException(
                    "Materialized-view module does not export memory.");
            _ = instance.GetFunction<int, int>("alloc")
                ?? throw new WasmMaterializedViewContractException(
                    "Materialized-view module does not export alloc.");
            _ = instance.GetAction<int, int>("dealloc") ?? instance.GetAction<int, int>("free")
                ?? throw new WasmMaterializedViewContractException(
                    "Materialized-view module does not export dealloc or free.");
            _ = instance.GetFunction<int, int, int, int, int, long>("mv_initialize")
                ?? throw new WasmMaterializedViewContractException(
                    "Materialized-view module does not export mv_initialize.");
            _ = instance.GetFunction<int, int, int, int, int, int, int, long>("mv_apply_event")
                ?? throw new WasmMaterializedViewContractException(
                    "Materialized-view module does not export mv_apply_event.");

            var initialize = instance.GetAction("_initialize") ?? instance.GetAction("_start");
            initialize?.Invoke();

            var metadataFunction = instance.GetFunction<long>("mv_metadata")
                ?? throw new WasmMaterializedViewContractException(
                    "Materialized-view module does not export mv_metadata.");
            var packed = metadataFunction.Invoke();
            var json = ReadPackedString(memory, packed);
            metadata = JsonSerializer.Deserialize<List<WasmMvMetadataDto>>(json, JsonOptions)
                ?? throw new WasmMaterializedViewContractException(
                    "mv_metadata returned an empty metadata document.");

            var dealloc = instance.GetAction<int, int>("dealloc") ?? instance.GetAction<int, int>("free");
            var ptr = unchecked((int)(packed >> 32));
            var len = unchecked((int)(packed & 0xFFFFFFFF));
            if (ptr != 0 && len != 0)
            {
                dealloc?.Invoke(ptr, len);
            }
        }

        ValidateMetadataCore(expected, metadata);
        return new WasmMaterializedViewValidationResult(modulePath, actualDigest, effectiveModuleBytes, metadata);
    }

    /// <summary>
    /// Computes the digest that the MV executor will use for a module artifact. For a core
    /// module this is the artifact itself; for a component this is the extracted core module
    /// passed to Wasmtime. AppHost manifest generation uses this same path so the declaration
    /// cannot accidentally describe component container bytes instead of instantiated bytes.
    /// </summary>
    public static string ComputeEffectiveModuleSha256(
        string modulePath,
        ulong? staticMemoryMaximumSizeBytes = 64UL * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        if (!File.Exists(modulePath))
        {
            throw new FileNotFoundException($"WASM module '{modulePath}' was not found.", modulePath);
        }

        var sourceBytes = File.ReadAllBytes(modulePath);
        using var runtime = new WasmtimeRuntime(new WasmtimeHostOptions
        {
            StaticMemoryMaximumSizeBytes = staticMemoryMaximumSizeBytes
        });
        var effectiveBytes = new WasmtimeModuleCache(runtime)
            .ReadEffectiveModuleBytes(modulePath, sourceBytes);
        return Convert.ToHexString(SHA256.HashData(effectiveBytes)).ToLowerInvariant();
    }

    /// <summary>
    /// Pure metadata gate exposed for mismatch-matrix tests. It has no filesystem or Wasmtime
    /// side effects and is also used by the startup validator after the real export call.
    /// </summary>
    public static void ValidateMetadata(
        IReadOnlyList<SekibanRuntimeMaterializedView> declarations,
        IReadOnlyList<WasmMvMetadataDto> metadata)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(metadata);
        ValidateMetadataCore(ValidateDeclarations("<metadata-only>", declarations), metadata);
    }

    private static List<SekibanRuntimeMaterializedView> ValidateDeclarations(
        string modulePath,
        IReadOnlyList<SekibanRuntimeMaterializedView> declarations)
    {
        var identities = new HashSet<(string Name, int Version)>();
        var digests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations)
        {
            if (string.IsNullOrWhiteSpace(declaration.ViewName) || declaration.ViewVersion <= 0)
            {
                throw new WasmMaterializedViewContractException(
                    "Every materialized-view declaration must have a non-empty name and positive version.");
            }

            if (!identities.Add((declaration.ViewName, declaration.ViewVersion)))
            {
                throw new WasmMaterializedViewContractException(
                    $"Materialized-view declaration '{declaration.ViewName}/{declaration.ViewVersion}' is duplicated.");
            }

            if (declaration.LogicalTables.Count == 0 ||
                declaration.LogicalTables.Any(string.IsNullOrWhiteSpace) ||
                declaration.LogicalTables.Count != declaration.LogicalTables.Distinct(StringComparer.Ordinal).Count())
            {
                throw new WasmMaterializedViewContractException(
                    $"Materialized-view declaration '{declaration.ViewName}/{declaration.ViewVersion}' has duplicate or missing logical tables.");
            }

            if (string.IsNullOrWhiteSpace(declaration.ModuleSha256) || declaration.ModuleSha256.Length != 64 ||
                !declaration.ModuleSha256.All(static c => Uri.IsHexDigit(c)))
            {
                throw new WasmMaterializedViewContractException(
                    $"Materialized-view declaration '{declaration.ViewName}/{declaration.ViewVersion}' must carry a full SHA-256 digest.");
            }

            if (!string.Equals(declaration.AbiVersion, WasmMvContract.AbiVersion, StringComparison.Ordinal))
            {
                throw new WasmMaterializedViewContractException(
                    $"Materialized-view declaration '{declaration.ViewName}/{declaration.ViewVersion}' has an unsupported ABI version.");
            }

            var capabilities = declaration.Capabilities.ToHashSet(StringComparer.Ordinal);
            if (capabilities.Count != declaration.Capabilities.Count ||
                !capabilities.SetEquals(WasmMvContract.SupportedCapabilities))
            {
                throw new WasmMaterializedViewContractException(
                    $"Materialized-view declaration '{declaration.ViewName}/{declaration.ViewVersion}' has an unsupported capability set.");
            }

            digests.Add(declaration.ModuleSha256);
        }

        if (digests.Count != 1)
        {
            throw new WasmMaterializedViewContractException(
                $"All materialized-view declarations for shared module '{modulePath}' must use the same full digest.");
        }

        return declarations.ToList();
    }

    private static void ValidateMetadataCore(
        IReadOnlyList<SekibanRuntimeMaterializedView> declarations,
        IReadOnlyList<WasmMvMetadataDto> metadata)
    {
        var actualByIdentity = new Dictionary<(string Name, int Version), WasmMvMetadataDto>();
        foreach (var item in metadata)
        {
            if (string.IsNullOrWhiteSpace(item.ViewName) || item.ViewVersion <= 0 ||
                !actualByIdentity.TryAdd((item.ViewName, item.ViewVersion), item))
            {
                throw new WasmMaterializedViewContractException(
                    "mv_metadata contains a malformed or duplicate view identity.");
            }

            if (!string.Equals(item.AbiVersion, WasmMvContract.AbiVersion, StringComparison.Ordinal))
            {
                throw new WasmMaterializedViewContractException(
                    $"mv_metadata for '{item.ViewName}/{item.ViewVersion}' has an unsupported ABI version.");
            }

            var capabilities = item.Capabilities.ToHashSet(StringComparer.Ordinal);
            if (capabilities.Count != item.Capabilities.Count ||
                !capabilities.SetEquals(WasmMvContract.SupportedCapabilities))
            {
                throw new WasmMaterializedViewContractException(
                    $"mv_metadata for '{item.ViewName}/{item.ViewVersion}' has an unsupported capability set.");
            }

            ValidateLogicalTables(item.ViewName, item.ViewVersion, item.LogicalTables);
            ValidateSchema(item);
        }

        var expectedByIdentity = declarations.ToDictionary(d => (d.ViewName, d.ViewVersion));
        if (actualByIdentity.Count != expectedByIdentity.Count ||
            actualByIdentity.Keys.Except(expectedByIdentity.Keys).Any() ||
            expectedByIdentity.Keys.Except(actualByIdentity.Keys).Any())
        {
            throw new WasmMaterializedViewContractException(
                "The WASM metadata view set does not exactly match the manifest declaration set.");
        }

        foreach (var ((name, version), declaration) in expectedByIdentity)
        {
            var actual = actualByIdentity[(name, version)];
            if (!actual.LogicalTables.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(declaration.LogicalTables))
            {
                throw new WasmMaterializedViewContractException(
                    $"WASM metadata logical tables for '{name}/{version}' do not match the manifest.");
            }

            if (!string.Equals(declaration.AbiVersion, actual.AbiVersion, StringComparison.Ordinal) ||
                !declaration.Capabilities.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(actual.Capabilities))
            {
                throw new WasmMaterializedViewContractException(
                    $"WASM metadata ABI or capabilities for '{name}/{version}' do not match the manifest.");
            }
        }
    }

    private static void ValidateLogicalTables(string viewName, int viewVersion, IReadOnlyList<string> tables)
    {
        if (tables.Count == 0 || tables.Any(string.IsNullOrWhiteSpace) ||
            tables.Count != tables.Distinct(StringComparer.Ordinal).Count())
        {
            throw new WasmMaterializedViewContractException(
                $"WASM metadata for '{viewName}/{viewVersion}' has duplicate or missing logical tables.");
        }
    }

    private static void ValidateSchema(WasmMvMetadataDto metadata)
    {
        if (metadata.Schema.Count == 0)
        {
            // This is the explicit verify-only deferral path for guests that have not yet added
            // provider-neutral schema metadata. CreateOrEnsure remains valid; VerifyOnly fails
            // closed with Sekiban's typed SchemaContractUnavailable result.
            return;
        }

        var tableNames = metadata.Schema.Select(table => table.LogicalTable).ToList();
        if (tableNames.Any(string.IsNullOrWhiteSpace) ||
            tableNames.Count != tableNames.Distinct(StringComparer.Ordinal).Count() ||
            !tableNames.ToHashSet(StringComparer.Ordinal).SetEquals(metadata.LogicalTables))
        {
            throw new WasmMaterializedViewContractException(
                $"WASM metadata schema for '{metadata.ViewName}/{metadata.ViewVersion}' does not exactly cover its logical tables.");
        }

        foreach (var table in metadata.Schema)
        {
            var columnNames = table.Columns.Select(column => column.Name).ToList();
            if (columnNames.Count == 0 || columnNames.Any(string.IsNullOrWhiteSpace) ||
                columnNames.Count != columnNames.Distinct(StringComparer.Ordinal).Count() ||
                table.PrimaryKeyColumns.Count == 0 ||
                table.PrimaryKeyColumns.Any(column => !columnNames.Contains(column, StringComparer.Ordinal)))
            {
                throw new WasmMaterializedViewContractException(
                    $"WASM metadata schema for table '{table.LogicalTable}' is malformed.");
            }

            if (table.Indexes.Any(index => string.IsNullOrWhiteSpace(index.Name) || index.Columns.Count == 0 ||
                                           index.Columns.Any(column => !columnNames.Contains(column, StringComparer.Ordinal))))
            {
                throw new WasmMaterializedViewContractException(
                    $"WASM metadata schema indexes for table '{table.LogicalTable}' are malformed.");
            }

            foreach (var column in table.Columns)
            {
                if (!Enum.IsDefined(column.TypeFamily))
                {
                    throw new WasmMaterializedViewContractException(
                        $"WASM metadata schema column '{table.LogicalTable}.{column.Name}' has an unsupported type family.");
                }
            }
        }
    }

    private static string ReadPackedString(Memory memory, long packed)
    {
        if (packed == 0)
        {
            throw new WasmMaterializedViewContractException("mv_metadata returned a null payload.");
        }

        var ptr = unchecked((int)(packed >> 32));
        var len = unchecked((int)(packed & 0xFFFFFFFF));
        if (ptr == 0 || len <= 0)
        {
            throw new WasmMaterializedViewContractException("mv_metadata returned an empty payload.");
        }

        try
        {
            return Encoding.UTF8.GetString(memory.GetSpan(ptr, len));
        }
        catch (Exception exception)
        {
            throw new WasmMaterializedViewContractException(
                "mv_metadata returned an invalid memory payload.", exception);
        }
    }
}

public sealed class WasmMaterializedViewValidationResult
{
    public WasmMaterializedViewValidationResult(
        string modulePath,
        string moduleSha256,
        byte[] moduleBytes,
        IReadOnlyList<WasmMvMetadataDto> metadata)
    {
        ModulePath = modulePath;
        ModuleSha256 = moduleSha256;
        ModuleBytes = moduleBytes.ToArray();
        Metadata = metadata.ToList();
    }

    public string ModulePath { get; }
    public string ModuleSha256 { get; }
    public byte[] ModuleBytes { get; }
    public IReadOnlyList<WasmMvMetadataDto> Metadata { get; }
    public string InstantiatedModuleSha256 =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ModuleBytes)).ToLowerInvariant();

    public static WasmMaterializedViewValidationResult ForTesting(string modulePath) =>
        new(modulePath, new string('0', 64), [], []);
}

public sealed class WasmMaterializedViewContractException : InvalidOperationException
{
    public WasmMaterializedViewContractException(string message) : base(message)
    {
    }

    public WasmMaterializedViewContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
