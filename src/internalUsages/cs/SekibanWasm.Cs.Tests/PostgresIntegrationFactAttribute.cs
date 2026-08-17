using Xunit;

namespace SekibanWasm.Cs.Tests;

/// <summary>
///     Marks a test that requires the disposable PostgreSQL service configured by CI. Local runs remain useful
///     without Docker or a database while CI always supplies the connection string and executes the test.
/// </summary>
public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public const string ConnectionStringEnvironmentVariable = "SEKIBAN_MV_TEST_POSTGRES_CONNECTION_STRING";

    public PostgresIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {ConnectionStringEnvironmentVariable} to run the PostgreSQL materialized-view integration test.";
        }
    }
}
