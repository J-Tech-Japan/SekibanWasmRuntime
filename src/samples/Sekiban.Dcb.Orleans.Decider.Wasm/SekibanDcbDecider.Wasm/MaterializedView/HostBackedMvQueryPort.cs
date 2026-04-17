using System.Runtime.InteropServices;
using System.Text.Json;

namespace SekibanDcbDecider.Wasm.MaterializedView;

/// <summary>
/// Implementation of <see cref="IWasmMvQueryPort"/> that calls back into the host via the
/// `mv_host_*` WASI imports. The host runs the query against Postgres using the apply-time
/// connection/transaction (captured in a host-side AsyncLocal) and returns the result as JSON.
/// </summary>
internal sealed class HostBackedMvQueryPort : IWasmMvQueryPort
{
    public static readonly HostBackedMvQueryPort Instance = new();

    private HostBackedMvQueryPort() { }

    public MvQueryRowDto? QuerySingleOrDefaultRow(string sql, IReadOnlyList<MvParam> parameters)
    {
        var resultJson = CallHostQuery(sql, parameters, rowLimit: 1);
        if (string.IsNullOrEmpty(resultJson))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize(resultJson, WasmJsonContext.Default.MvQueryResultDto);
        return result?.Rows.FirstOrDefault();
    }

    public IReadOnlyList<MvQueryRowDto> QueryRows(string sql, IReadOnlyList<MvParam> parameters)
    {
        var resultJson = CallHostQuery(sql, parameters, rowLimit: null);
        if (string.IsNullOrEmpty(resultJson))
        {
            return Array.Empty<MvQueryRowDto>();
        }

        var result = JsonSerializer.Deserialize(resultJson, WasmJsonContext.Default.MvQueryResultDto);
        return result?.Rows ?? (IReadOnlyList<MvQueryRowDto>)Array.Empty<MvQueryRowDto>();
    }

    public string? ExecuteScalarJson(string sql, IReadOnlyList<MvParam> parameters)
    {
        var row = QuerySingleOrDefaultRow(sql, parameters);
        if (row is null) return null;
        return row.Columns.Values.FirstOrDefault();
    }

    private static unsafe string CallHostQuery(string sql, IReadOnlyList<MvParam> parameters, int? rowLimit)
    {
        var paramsJson = JsonSerializer.Serialize(
            parameters.ToList(),
            WasmJsonContext.Default.ListMvParam);

        byte[] sqlBytes = System.Text.Encoding.UTF8.GetBytes(sql);
        byte[] paramsBytes = System.Text.Encoding.UTF8.GetBytes(paramsJson);

        fixed (byte* sqlPtr = sqlBytes)
        fixed (byte* paramsPtr = paramsBytes)
        {
            long packed = MvHostImports.mv_host_query_rows(
                (int)(IntPtr)sqlPtr, sqlBytes.Length,
                (int)(IntPtr)paramsPtr, paramsBytes.Length,
                rowLimit ?? -1);

            int resultPtr = (int)(packed >> 32);
            int resultLen = (int)(uint)packed;
            if (resultPtr == 0 || resultLen <= 0)
            {
                return string.Empty;
            }

            // The host allocated the result buffer via the WASM `alloc` export so we own it and
            // must free it after copying. See WasmExports.Alloc/Dealloc.
            var result = System.Text.Encoding.UTF8.GetString((byte*)(IntPtr)resultPtr, resultLen);
            NativeMemory.Free((void*)(IntPtr)resultPtr);
            return result;
        }
    }
}

/// <summary>
/// WASI imports provided by the host for materialized view query callbacks. Return value is a
/// packed (ptr, len) long — ptr is an allocation owned by WASM (created by `alloc`) that the
/// caller must free after copying.
/// </summary>
internal static partial class MvHostImports
{
    [LibraryImport("env", EntryPoint = "mv_host_query_rows")]
    internal static partial long mv_host_query_rows(
        int sqlPtr,
        int sqlLen,
        int paramsPtr,
        int paramsLen,
        int rowLimit);
}
