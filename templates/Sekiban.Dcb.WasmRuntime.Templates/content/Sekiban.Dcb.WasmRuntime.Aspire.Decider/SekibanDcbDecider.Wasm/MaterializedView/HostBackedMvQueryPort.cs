using System.Runtime.InteropServices;
using System.Text.Json;

namespace SekibanDcbDecider.Wasm.MaterializedView;

/// <summary>
/// Implementation of <see cref="IWasmMvQueryPort"/> that calls back into the host via the
/// `mv_host_*` WASI imports. The host runs the query against Postgres using the apply-time
/// connection/transaction and returns the result as JSON.
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

        ThrowIfHostError(resultJson);
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

        ThrowIfHostError(resultJson);
        var result = JsonSerializer.Deserialize(resultJson, WasmJsonContext.Default.MvQueryResultDto);
        return result?.Rows ?? (IReadOnlyList<MvQueryRowDto>)Array.Empty<MvQueryRowDto>();
    }

    public string? ExecuteScalarJson(string sql, IReadOnlyList<MvParam> parameters)
    {
        var row = QuerySingleOrDefaultRow(sql, parameters);
        if (row is null) return null;
        if (row.Columns.Count == 0) return null;
        if (row.Columns.Count != 1)
        {
            throw new InvalidOperationException(
                "ExecuteScalarJson requires the query to return exactly one column.");
        }
        return row.Columns.Single().Value;
    }

    // The host import returns `{"error":"..."}` when the apply-time Dapper query fails. Detect the
    // envelope eagerly and throw so MV catch-up fails and retries rather than projecting "no rows".
    private static void ThrowIfHostError(string resultJson)
    {
        if (resultJson.Length < 2 || resultJson[0] != '{') return;
        using var document = System.Text.Json.JsonDocument.Parse(resultJson);
        if (document.RootElement.TryGetProperty("error", out var err))
        {
            var message = err.ValueKind == System.Text.Json.JsonValueKind.String
                ? err.GetString()
                : err.GetRawText();
            throw new InvalidOperationException(
                $"mv_host_query_rows returned an error envelope: {message}");
        }
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
            // must free it after copying.
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
