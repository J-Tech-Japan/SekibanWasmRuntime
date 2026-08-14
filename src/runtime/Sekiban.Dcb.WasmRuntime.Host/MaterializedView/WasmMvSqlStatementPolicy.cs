using System.Text;
using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>Exact identity and table scope attached to one WASM query callback.</summary>
public sealed record WasmMvQueryCallbackContext(
    string ServiceId,
    string ViewName,
    int ViewVersion,
    IReadOnlyList<MvTable> Tables);

internal static class WasmMvQueryCallbackScope
{
    private static readonly AsyncLocal<WasmMvQueryCallbackContext?> CurrentValue = new();

    public static WasmMvQueryCallbackContext? Current => CurrentValue.Value;

    public static IDisposable Push(WasmMvQueryCallbackContext context)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = context;
        return new Scope(() => CurrentValue.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

/// <summary>
/// Host-owned least-privilege policy for SQL crossing the WASM query-callback boundary. The
/// released DCB policy wrapper invokes this for returned statements as part of its 10.14
/// enforcement path; non-query origins delegate to DCB's released allow-all compatibility policy
/// rather than reimplementing Sekiban's initialization/apply statement policy here. The executor
/// also invokes the query form immediately before the callback's DB port call so a direct/fake
/// port cannot bypass the same decision.
/// </summary>
public sealed class WasmMvSqlStatementPolicy : IMvSqlStatementPolicy
{
    private static readonly HashSet<string> TransactionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN", "START", "COMMIT", "ROLLBACK", "SAVEPOINT", "RELEASE", "SET", "RESET", "LOCK"
    };

    private static readonly HashSet<string> QueryForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER", "DROP", "TRUNCATE", "GRANT", "REVOKE",
        "CALL", "EXEC", "DO", "COPY", "VACUUM", "ANALYZE", "ATTACH", "DETACH", "PRAGMA", "INTO"
    };

    private static readonly HashSet<string> ForbiddenCatalogNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "information_schema", "pg_catalog", "sqlite_master", "sqlite_schema", "sekiban_mv_registry",
        "mv_registry", "event_store", "events"
    };

    public const int MaxQueryRows = WasmMvContract.MaxQueryRows;

    public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
        MvSqlStatementContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Origin != MvSqlStatementOrigin.ProjectorQuery)
        {
            return MvAllowAllSqlStatementPolicy.Instance.EvaluateAsync(context, cancellationToken);
        }

        try
        {
            Validate(context);
            // DCB's released policy evaluator treats a non-empty reason on an allow decision as
            // an invalid decision. Keep the reason-free allow contract here; rejection details
            // still carry the stable rule id below.
            return ValueTask.FromResult(MvSqlPolicyDecision.Allow());
        }
        catch (WasmMvQueryPolicyException exception)
        {
            return ValueTask.FromResult(MvSqlPolicyDecision.Reject(exception.Message, exception.RuleId));
        }
    }

    public static void ValidateQuery(
        WasmMvQueryCallbackContext context,
        string sql,
        IReadOnlyList<MvParam> parameters,
        int rowLimit)
    {
        var decision = new WasmMvSqlStatementPolicy().EvaluateAsync(
            new MvSqlStatementContext(
                context.ServiceId,
                context.ViewName,
                context.ViewVersion,
                MvSqlStatementPhase.Apply,
                context.Tables,
                sql,
                parameters)
            {
                Origin = MvSqlStatementOrigin.ProjectorQuery
            }).GetAwaiter().GetResult();

        if (!decision.IsAllowed)
        {
            throw new WasmMvQueryPolicyException(
                decision.Reason ?? "The host query policy rejected the statement.",
                decision.RuleId ?? "query-policy");
        }

        if (rowLimit <= 0 || rowLimit > MaxQueryRows)
        {
            throw new WasmMvQueryPolicyException(
                "The host query policy requires a positive bounded row limit.",
                "query-row-limit");
        }

        var tokens = SqlTokenizer.Tokenize(sql);
        if (!HasTopLevelKeyword(tokens, "LIMIT"))
        {
            // The executor appends a literal bound before handing the statement to DCB's
            // policy wrapper. This method only validates the caller's untrusted SQL.
            return;
        }

        var limitIndex = IndexOfTopLevelKeyword(tokens, "LIMIT");
        if (limitIndex + 1 >= tokens.Count || !int.TryParse(tokens[limitIndex + 1], out var declaredLimit) ||
            declaredLimit <= 0 || declaredLimit > rowLimit || declaredLimit > MaxQueryRows)
        {
            throw new WasmMvQueryPolicyException(
                "The host query policy requires a positive literal row bound within the configured maximum.",
                "query-row-limit");
        }
    }

    public static string EnsureBoundedQuery(string sql, int rowLimit)
    {
        if (rowLimit <= 0 || rowLimit > MaxQueryRows)
        {
            throw new WasmMvQueryPolicyException(
                "The host query policy requires a positive bounded row limit.",
                "query-row-limit");
        }

        var tokens = SqlTokenizer.Tokenize(sql);
        var limitIndex = IndexOfTopLevelKeyword(tokens, "LIMIT");
        if (limitIndex >= 0)
        {
            if (limitIndex + 1 >= tokens.Count || !int.TryParse(tokens[limitIndex + 1], out var declaredLimit) ||
                declaredLimit <= 0 || declaredLimit > rowLimit || declaredLimit > MaxQueryRows)
            {
                throw new WasmMvQueryPolicyException(
                    "The host query policy requires a positive literal row bound within the requested maximum.",
                    "query-row-limit");
            }

            return sql;
        }

        return sql.TrimEnd().TrimEnd(';').TrimEnd() + $" LIMIT {rowLimit}";
    }

    private static void Validate(MvSqlStatementContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ServiceId) || string.IsNullOrWhiteSpace(context.ViewName) ||
            context.ViewVersion <= 0)
        {
            throw new WasmMvQueryPolicyException("The host query context is incomplete.", "query-context");
        }

        var tokens = SqlTokenizer.Tokenize(context.Sql);
        if (tokens.Count == 0)
        {
            throw new WasmMvQueryPolicyException("The host SQL statement is empty.", "sql-empty");
        }

        var first = tokens[0];
        if (context.Origin == MvSqlStatementOrigin.ProjectorQuery)
        {
            if (!string.Equals(first, "SELECT", StringComparison.OrdinalIgnoreCase))
            {
                throw new WasmMvQueryPolicyException(
                    "Materialized-view callbacks may issue only a SELECT statement.",
                    "query-read-only");
            }

            if (tokens.Any(token => QueryForbiddenKeywords.Contains(token) || TransactionKeywords.Contains(token)))
            {
                throw new WasmMvQueryPolicyException(
                    "Materialized-view callbacks may not issue DDL, DML, transaction control, or catalog statements.",
                    "query-read-only");
            }

            if (!HasTopLevelKeyword(tokens, "FROM") && !HasTopLevelKeyword(tokens, "JOIN"))
            {
                throw new WasmMvQueryPolicyException(
                    "Materialized-view callbacks must read a current-view physical table.",
                    "query-table-scope");
            }
        }
        else
        {
            // EvaluateAsync delegates non-query statements to DCB's released compatibility
            // policy. Keep this branch for the private validator's complete context handling.
            return;
        }

        ValidateTableScope(context, tokens);
        ValidateParameters(tokens, context.Parameters);

        if (context.Origin == MvSqlStatementOrigin.ProjectorQuery && HasTopLevelKeyword(tokens, "LIMIT"))
        {
            var limitIndex = IndexOfTopLevelKeyword(tokens, "LIMIT");
            if (limitIndex + 1 >= tokens.Count || !int.TryParse(tokens[limitIndex + 1], out var limit) ||
                limit <= 0 || limit > MaxQueryRows)
            {
                throw new WasmMvQueryPolicyException(
                    "The host query policy requires a positive literal row bound within the configured maximum.",
                    "query-row-limit");
            }
        }
    }

    private static void ValidateTableScope(MvSqlStatementContext context, IReadOnlyList<string> tokens)
    {
        var allowed = context.Tables
            .Select(table => table.PhysicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0)
        {
            throw new WasmMvQueryPolicyException("The host query context has no physical table bindings.", "query-table-scope");
        }

        if (tokens.Any(token => ForbiddenCatalogNames.Contains(token)))
        {
            throw new WasmMvQueryPolicyException(
                "Materialized-view SQL may not access framework or catalog tables.",
                "table-framework-scope");
        }

        var references = new List<string>();
        for (var index = 0; index < tokens.Count; index++)
        {
            var keyword = tokens[index];
            if (string.Equals(keyword, "FROM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(keyword, "JOIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(keyword, "UPDATE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(keyword, "INTO", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(keyword, "TABLE", StringComparison.OrdinalIgnoreCase))
            {
                var next = index + 1;
                while (next < tokens.Count &&
                       (string.Equals(tokens[next], "IF", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tokens[next], "NOT", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tokens[next], "EXISTS", StringComparison.OrdinalIgnoreCase)))
                {
                    next++;
                }

                if (next >= tokens.Count || tokens[next] == "(")
                {
                    throw new WasmMvQueryPolicyException(
                        "Materialized-view SQL contains an unresolved table reference.",
                        "table-reference");
                }

                references.Add(tokens[next]);
            }

            if (string.Equals(keyword, "ON", StringComparison.OrdinalIgnoreCase) &&
                tokens.Take(index).Any(token => string.Equals(token, "INDEX", StringComparison.OrdinalIgnoreCase)))
            {
                if (index + 1 < tokens.Count)
                {
                    references.Add(tokens[index + 1]);
                }
            }

            if (string.Equals(keyword, ",", StringComparison.Ordinal) && index > 0)
            {
                var previousKeyword = tokens.Take(index).LastOrDefault(token =>
                    string.Equals(token, "FROM", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(token, "JOIN", StringComparison.OrdinalIgnoreCase));
                if (previousKeyword is not null && index + 1 < tokens.Count)
                {
                    references.Add(tokens[index + 1]);
                }
            }
        }

        foreach (var reference in references)
        {
            if (!allowed.Contains(reference) || reference.Contains('.', StringComparison.Ordinal))
            {
                throw new WasmMvQueryPolicyException(
                    "Materialized-view SQL references a table outside the current view.",
                    "table-view-scope");
            }
        }

        if (context.Origin == MvSqlStatementOrigin.ProjectorQuery && references.Count == 0)
        {
            throw new WasmMvQueryPolicyException(
                "Materialized-view callbacks must read a current-view physical table.",
                "query-table-scope");
        }
    }

    private static void ValidateParameters(IReadOnlyList<string> tokens, IReadOnlyList<MvParam> parameters)
    {
        var names = parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens.Where(token => token.Length > 1 &&
                                                     (token[0] is '@' or ':' or '$')))
        {
            if (token.StartsWith("::", StringComparison.Ordinal))
            {
                continue;
            }

            var name = token[1..];
            if (!names.Contains(name))
            {
                throw new WasmMvQueryPolicyException(
                    "Materialized-view SQL references a parameter that was not supplied.",
                    "query-parameters");
            }
        }
    }

    private static bool HasTopLevelKeyword(IReadOnlyList<string> tokens, string keyword) =>
        IndexOfTopLevelKeyword(tokens, keyword) >= 0;

    private static int IndexOfTopLevelKeyword(IReadOnlyList<string> tokens, string keyword)
    {
        var depth = 0;
        for (var index = 0; index < tokens.Count; index++)
        {
            switch (tokens[index])
            {
                case "(": depth++; break;
                case ")": depth = Math.Max(0, depth - 1); break;
                default:
                    if (depth == 0 && string.Equals(tokens[index], keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return index;
                    }
                    break;
            }
        }

        return -1;
    }

    private static class SqlTokenizer
    {
        public static IReadOnlyList<string> Tokenize(string sql)
        {
            var tokens = new List<string>();
            for (var index = 0; index < sql.Length;)
            {
                var character = sql[index];
                if (char.IsWhiteSpace(character))
                {
                    index++;
                    continue;
                }

                if (character == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
                {
                    throw new WasmMvQueryPolicyException("SQL comments are not allowed across the WASM boundary.", "sql-comments");
                }

                if (character == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
                {
                    throw new WasmMvQueryPolicyException("SQL comments are not allowed across the WASM boundary.", "sql-comments");
                }

                if (character == '\'')
                {
                    index++;
                    while (index < sql.Length)
                    {
                        if (sql[index] == '\'' && index + 1 < sql.Length && sql[index + 1] == '\'')
                        {
                            index += 2;
                            continue;
                        }

                        if (sql[index++] == '\'') break;
                    }
                    continue;
                }

                if (character == '"')
                {
                    var start = ++index;
                    var builder = new StringBuilder();
                    while (index < sql.Length)
                    {
                        if (sql[index] == '"' && index + 1 < sql.Length && sql[index + 1] == '"')
                        {
                            builder.Append(sql[start..index]);
                            builder.Append('"');
                            index += 2;
                            start = index;
                            continue;
                        }

                        if (sql[index++] == '"')
                        {
                            builder.Append(sql[start..(index - 1)]);
                            break;
                        }
                    }
                    tokens.Add(builder.ToString());
                    continue;
                }

                if (char.IsLetter(character) || character == '_')
                {
                    var start = index++;
                    while (index < sql.Length &&
                           (char.IsLetterOrDigit(sql[index]) || sql[index] is '_' or '$')) index++;
                    tokens.Add(sql[start..index]);
                    continue;
                }

                if (character is '@' or ':' or '$')
                {
                    if (character == ':' && index + 1 < sql.Length && sql[index + 1] == ':')
                    {
                        tokens.Add("::");
                        index += 2;
                        continue;
                    }

                    var start = index++;
                    while (index < sql.Length &&
                           (char.IsLetterOrDigit(sql[index]) || sql[index] == '_')) index++;
                    tokens.Add(sql[start..index]);
                    continue;
                }

                tokens.Add(character.ToString());
                index++;
            }

            var semicolonCount = tokens.Count(token => token == ";");
            if (semicolonCount > 1 || semicolonCount == 1 && tokens[^1] != ";")
            {
                throw new WasmMvQueryPolicyException("Multiple SQL statements are not allowed.", "sql-statements");
            }

            return tokens.Where(token => token != ";").ToList();
        }
    }
}

public sealed class WasmMvQueryPolicyException : InvalidOperationException
{
    public WasmMvQueryPolicyException(string message, string ruleId) : base(message)
    {
        RuleId = ruleId;
    }

    public string RuleId { get; }
}
