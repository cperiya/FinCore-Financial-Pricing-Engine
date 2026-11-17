namespace FinCore.IntegrationTests;

/// <summary>
/// Marks an integration test that runs only when a SQL Server test connection is configured.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SqlServerFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a SQL Server integration fact and skips it when no test connection is configured.
    /// </summary>
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("FINCORE_SQLSERVER_TEST_CONNECTION_STRING")))
        {
            Skip = "Set FINCORE_SQLSERVER_TEST_CONNECTION_STRING to run SQL Server integration tests.";
        }
    }
}
