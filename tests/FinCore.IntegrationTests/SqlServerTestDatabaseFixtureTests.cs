using Microsoft.Data.SqlClient;

namespace FinCore.IntegrationTests;

/// <summary>
/// Verifies safe connection validation performed before the SQL fixture connects to a server.
/// </summary>
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SqlServerTestDatabaseFixtureTests
{
    private const string ConnectionStringEnvironmentVariable = "FINCORE_SQLSERVER_TEST_CONNECTION_STRING";

    /// <summary>
    /// Verifies the fixture rejects the Windows localhost endpoint before opening a SQL connection.
    /// </summary>
    /// <returns>A task that completes after the validation assertion.</returns>
    [Fact]
    public async Task InitializeAsync_WithLocalhostEndpoint_RejectsConnectionBeforeConnecting()
    {
        string? originalConnectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        SqlConnectionStringBuilder connectionBuilder = new()
        {
            DataSource = "localhost,1433",
            InitialCatalog = "FinCoreIntegrationTests",
            UserID = "sa",
            Password = "test-only-not-a-credential",
            Encrypt = true,
            TrustServerCertificate = true
        };
        Environment.SetEnvironmentVariable(
            ConnectionStringEnvironmentVariable,
            connectionBuilder.ConnectionString);

        try
        {
            SqlServerTestDatabaseFixture fixture = new();
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                fixture.InitializeAsync);

            Assert.Contains("127.0.0.1,1433", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                ConnectionStringEnvironmentVariable,
                originalConnectionString);
        }
    }
}
