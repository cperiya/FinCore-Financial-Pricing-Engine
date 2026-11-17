namespace FinCore.IntegrationTests;

/// <summary>
/// Defines the non-parallel SQL Server integration-test collection.
/// </summary>
[CollectionDefinition("FinCore SQL Server integration", DisableParallelization = true)]
public sealed class SqlServerIntegrationCollection : ICollectionFixture<SqlServerTestDatabaseFixture>
{
    /// <summary>
    /// Gets the xUnit collection name used by SQL Server integration tests.
    /// </summary>
    public const string Name = "FinCore SQL Server integration";
}
