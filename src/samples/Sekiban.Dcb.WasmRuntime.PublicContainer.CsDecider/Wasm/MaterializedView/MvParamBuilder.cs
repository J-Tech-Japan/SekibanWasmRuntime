using System.Text;

namespace PublicContainerCsDecider.Wasm.MaterializedView;

/// <summary>
/// Ergonomic builder for SQL parameter lists that need to cross the WASM boundary, with explicit
/// type tagging so the host can rehydrate Dapper parameters without reflection.
/// </summary>
public sealed class MvParamBuilder
{
    private readonly List<MvParam> _items = new();

    public MvParamBuilder String(string name, string? value) => AddString(name, value);
    public MvParamBuilder Guid(string name, Guid value) => AddGuid(name, value);
    public MvParamBuilder Int32(string name, int value) => AddInt32(name, value);
    public MvParamBuilder Int64(string name, long value) => AddInt64(name, value);
    public MvParamBuilder Bool(string name, bool value) => AddBool(name, value);
    public MvParamBuilder Decimal(string name, decimal value) => AddDecimal(name, value);
    public MvParamBuilder Double(string name, double value) => AddDouble(name, value);
    public MvParamBuilder DateTimeOffset(string name, DateTimeOffset value) => AddDateTimeOffset(name, value);
    public MvParamBuilder Bytes(string name, byte[] value) => AddBytes(name, value);
    public MvParamBuilder Null(string name) => AddNull(name);

    public List<MvParam> Build() => _items;

    public static implicit operator List<MvParam>(MvParamBuilder builder) => builder.Build();

    private MvParamBuilder AddString(string name, string? value)
    {
        if (value is null) return AddNull(name);
        _items.Add(new MvParam { Name = name, Kind = MvParamKind.String, ValueJson = EscapeJsonString(value) });
        return this;
    }

    private MvParamBuilder AddGuid(string name, Guid value)
    {
        _items.Add(new MvParam { Name = name, Kind = MvParamKind.Guid, ValueJson = EscapeJsonString(value.ToString()) });
        return this;
    }

    private MvParamBuilder AddInt32(string name, int value)
    {
        _items.Add(new MvParam { Name = name, Kind = MvParamKind.Int32, ValueJson = value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        return this;
    }

    private MvParamBuilder AddInt64(string name, long value)
    {
        _items.Add(new MvParam { Name = name, Kind = MvParamKind.Int64, ValueJson = value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        return this;
    }

    private MvParamBuilder AddBool(string name, bool value)
    {
        _items.Add(new MvParam { Name = name, Kind = MvParamKind.Boolean, ValueJson = value ? "true" : "false" });
        return this;
    }

    private MvParamBuilder AddDecimal(string name, decimal value)
    {
        _items.Add(new MvParam { Name = name, Kind = MvParamKind.Decimal, ValueJson = value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        return this;
    }

    private MvParamBuilder AddDouble(string name, double value)
    {
        _items.Add(new MvParam { Name = name, Kind = MvParamKind.Double, ValueJson = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) });
        return this;
    }

    private MvParamBuilder AddDateTimeOffset(string name, DateTimeOffset value)
    {
        _items.Add(new MvParam
        {
            Name = name,
            Kind = MvParamKind.DateTimeOffset,
            ValueJson = EscapeJsonString(value.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
        });
        return this;
    }

    private MvParamBuilder AddBytes(string name, byte[] value)
    {
        _items.Add(new MvParam
        {
            Name = name,
            Kind = MvParamKind.Bytes,
            ValueJson = EscapeJsonString(Convert.ToBase64String(value))
        });
        return this;
    }

    private MvParamBuilder AddNull(string name)
    {
        _items.Add(new MvParam { Name = name, Kind = MvParamKind.Null, ValueJson = null });
        return this;
    }

    // AOT-friendly JSON string literal producer.
    internal static string EscapeJsonString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}

public static class MvQueryRowReader
{
    public static Guid GetGuid(MvQueryRowDto row, string columnName) =>
        System.Guid.Parse(RequireString(row, columnName));

    public static string GetString(MvQueryRowDto row, string columnName) =>
        RequireString(row, columnName);

    public static int GetInt32(MvQueryRowDto row, string columnName) =>
        int.Parse(RequireString(row, columnName), System.Globalization.CultureInfo.InvariantCulture);

    public static long GetInt64(MvQueryRowDto row, string columnName) =>
        long.Parse(RequireString(row, columnName), System.Globalization.CultureInfo.InvariantCulture);

    public static bool GetBool(MvQueryRowDto row, string columnName) =>
        bool.Parse(RequireString(row, columnName));

    public static string? GetStringOrNull(MvQueryRowDto row, string columnName) =>
        row.Columns.TryGetValue(columnName, out var value) ? value : null;

    public static bool IsNull(MvQueryRowDto row, string columnName) =>
        !row.Columns.TryGetValue(columnName, out var value) || value is null;

    private static string RequireString(MvQueryRowDto row, string columnName)
    {
        if (!row.Columns.TryGetValue(columnName, out var value) || value is null)
        {
            throw new InvalidOperationException($"Column '{columnName}' is null or missing.");
        }
        return value;
    }
}
