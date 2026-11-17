using System.Data;
using FinCore.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FinCore.IntegrationTests;

/// <summary>
/// Creates and owns a uniquely named SQL Server database for one integration-test run.
/// </summary>
public sealed class SqlServerTestDatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvironmentVariable = "FINCORE_SQLSERVER_TEST_CONNECTION_STRING";
    private const string OwnershipPropertyName = "FinCoreTestDatabaseOwner";
    private const string DatabasePrefix = "FinCoreTestRun_";
    private const string ExistingTestDatabaseName = "FinCoreIntegrationTests";
    private const string DevelopmentDatabaseName = "FinCore";

    private readonly Guid _runId = Guid.NewGuid();
    private string? _masterConnectionString;
    private string? _testConnectionString;
    private bool _databaseCreated;
    private bool _ownershipMarkerRecorded;

    /// <summary>
    /// Gets the exact generated database name owned by this fixture.
    /// </summary>
    public string DatabaseName => $"{DatabasePrefix}{_runId:N}";

    /// <summary>
    /// Gets the connection string for this fixture's isolated database.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the SQL Server integration connection was not configured.
    /// </exception>
    public string TestConnectionString =>
        _testConnectionString ??
        throw new InvalidOperationException(
            $"Set {ConnectionStringEnvironmentVariable} to run SQL Server integration tests.");

    /// <summary>
    /// Validates the local SQL Server, creates the owned database, records its owner, and applies migrations.
    /// </summary>
    /// <returns>A task that completes after migration application.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the connection is not the expected local SQL Server 2022 Developer instance or a safe
    /// temporary database cannot be established.
    /// </exception>
    public async Task InitializeAsync()
    {
        string? configuredConnectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            return;
        }

        SqlConnectionStringBuilder connectionBuilder;
        try
        {
            connectionBuilder = new SqlConnectionStringBuilder(configuredConnectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} is not a valid SQL Server connection string.",
                exception);
        }

        ValidateConfiguredConnection(connectionBuilder);

        connectionBuilder.InitialCatalog = "master";
        _masterConnectionString = connectionBuilder.ConnectionString;
        connectionBuilder.InitialCatalog = DatabaseName;
        connectionBuilder.ApplicationName = $"FinCore.IntegrationTests/{_runId:N}";
        _testConnectionString = connectionBuilder.ConnectionString;

        await ValidateLocalSqlServerAsync(_masterConnectionString);
        await CreateDatabaseAsync(_masterConnectionString);
        _databaseCreated = true;

        try
        {
            await RecordOwnershipMarkerAsync(TestConnectionString);
            _ownershipMarkerRecorded = true;
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                $"Created temporary database '{DatabaseName}' but could not record its ownership marker. " +
                "The fixture will not drop a database whose ownership cannot be verified.",
                exception);
        }

        DbContextOptions<FinCoreDbContext> options = new DbContextOptionsBuilder<FinCoreDbContext>()
            .UseSqlServer(
                TestConnectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly(
                    typeof(FinCoreDbContext).Assembly.GetName().Name))
            .Options;

        await using FinCoreDbContext dbContext = new(options);
        await dbContext.Database.MigrateAsync();
    }

    /// <summary>
    /// Drops only this fixture's exact generated database after revalidating the SQL Server and owner marker.
    /// </summary>
    /// <returns>A task that completes after safe cleanup.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when database ownership or the expected local SQL Server cannot be verified.
    /// </exception>
    public async Task DisposeAsync()
    {
        if (!_databaseCreated)
        {
            return;
        }

        if (!_ownershipMarkerRecorded)
        {
            throw new InvalidOperationException(
                $"Temporary database '{DatabaseName}' may exist, but its ownership marker was not recorded. " +
                "It was not dropped.");
        }

        VerifyGeneratedDatabaseIdentity();
        string masterConnectionString = _masterConnectionString ??
            throw new InvalidOperationException(
                $"Cannot verify ownership for temporary database '{DatabaseName}'; it was not dropped.");

        await ValidateLocalSqlServerAsync(masterConnectionString);
        await DropOwnedDatabaseAsync(masterConnectionString);
        _databaseCreated = false;
        _ownershipMarkerRecorded = false;
    }

    private static void ValidateConfiguredConnection(SqlConnectionStringBuilder connectionBuilder)
    {
        if (!string.Equals(
                connectionBuilder.InitialCatalog,
                ExistingTestDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The test connection must name '{ExistingTestDatabaseName}' as its credential source. " +
                $"The fixture will use only master and its generated {DatabasePrefix}<GUID> database.");
        }

        if (!IsLocalSqlServerEndpoint(connectionBuilder.DataSource))
        {
            throw new InvalidOperationException(
                "SQL Server integration tests require Data Source 127.0.0.1,1433. " +
                "Do not use localhost, which may resolve to unavailable IPv6 in the Windows Docker setup.");
        }

        if (!connectionBuilder.Encrypt ||
            !connectionBuilder.TrustServerCertificate ||
            connectionBuilder.IntegratedSecurity ||
            string.IsNullOrWhiteSpace(connectionBuilder.UserID))
        {
            throw new InvalidOperationException(
                "SQL Server integration tests require encrypted SQL authentication for the local development instance.");
        }
    }

    private static bool IsLocalSqlServerEndpoint(string dataSource)
    {
        string normalized = dataSource.Trim();
        if (normalized.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..];
        }

        return normalized.Equals("127.0.0.1,1433", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ValidateLocalSqlServerAsync(string masterConnectionString)
    {
        await using SqlConnection connection = new(masterConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                DB_NAME(),
                CONVERT(nvarchar(32), SERVERPROPERTY('ProductMajorVersion')),
                CONVERT(nvarchar(128), SERVERPROPERTY('Edition')),
                IS_SRVROLEMEMBER(N'sysadmin');
            """;

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                "Could not verify the local SQL Server instance; no database operation was attempted.");
        }

        string databaseName = reader.GetString(0);
        string productMajorVersion = reader.GetString(1);
        string edition = reader.GetString(2);
        int isSysAdmin = reader.GetInt32(3);

        if (!string.Equals(databaseName, "master", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(productMajorVersion, "16", StringComparison.Ordinal) ||
            !edition.Contains("Developer Edition", StringComparison.OrdinalIgnoreCase) ||
            isSysAdmin != 1)
        {
            throw new InvalidOperationException(
                "Refusing database operations: the connection is not to a sysadmin-enabled SQL Server 2022 " +
                "Developer Edition instance on the configured local Docker endpoint.");
        }
    }

    private async Task CreateDatabaseAsync(string masterConnectionString)
    {
        VerifyGeneratedDatabaseIdentity();

        await using SqlConnection connection = new(masterConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            IF @DatabaseName IN (N'FinCore', N'FinCoreIntegrationTests')
                THROW 51000, 'Refusing to create a protected database name.', 1;
            IF DB_ID(@DatabaseName) IS NOT NULL
                THROW 51001, 'The generated test database name already exists.', 1;
            DECLARE @CreateDatabase nvarchar(max) =
                N'CREATE DATABASE ' + QUOTENAME(@DatabaseName);
            EXEC sys.sp_executesql @CreateDatabase;
            """;
        command.Parameters.Add("@DatabaseName", SqlDbType.NVarChar, 128).Value = DatabaseName;
        await command.ExecuteNonQueryAsync();
    }

    private async Task RecordOwnershipMarkerAsync(string testConnectionString)
    {
        await using SqlConnection connection = new(testConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_addextendedproperty
                @name = @PropertyName,
                @value = @OwnerId;
            """;
        command.Parameters.Add("@PropertyName", SqlDbType.NVarChar, 128).Value = OwnershipPropertyName;
        command.Parameters.Add("@OwnerId", SqlDbType.NVarChar, 32).Value = _runId.ToString("N");
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropOwnedDatabaseAsync(string masterConnectionString)
    {
        VerifyGeneratedDatabaseIdentity();

        await using SqlConnection connection = new(masterConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @DatabaseName sysname = @RequestedDatabaseName;
            IF @DatabaseName IN (N'FinCore', N'FinCoreIntegrationTests')
                THROW 51002, 'Refusing to drop a protected database name.', 1;
            IF DB_ID(@DatabaseName) IS NULL
                THROW 51003, 'The owned test database is missing; cleanup ownership cannot be verified.', 1;

            DECLARE @VerifyOwner nvarchar(max) =
                N'IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@DatabaseName) +
                N'.sys.extended_properties WHERE class = 0 AND name = @PropertyName ' +
                N'AND CONVERT(nvarchar(128), value) = @OwnerId) ' +
                N'THROW 51004, ''Ownership marker does not match this test run.'', 1;';
            EXEC sys.sp_executesql
                @VerifyOwner,
                N'@PropertyName nvarchar(128), @OwnerId nvarchar(32)',
                @PropertyName = @ExpectedPropertyName,
                @OwnerId = @ExpectedOwnerId;

            DECLARE @DropDatabase nvarchar(max) =
                N'ALTER DATABASE ' + QUOTENAME(@DatabaseName) +
                N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' +
                QUOTENAME(@DatabaseName);
            EXEC sys.sp_executesql @DropDatabase;
            """;
        command.Parameters.Add("@RequestedDatabaseName", SqlDbType.NVarChar, 128).Value = DatabaseName;
        command.Parameters.Add("@ExpectedPropertyName", SqlDbType.NVarChar, 128).Value = OwnershipPropertyName;
        command.Parameters.Add("@ExpectedOwnerId", SqlDbType.NVarChar, 32).Value = _runId.ToString("N");

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException exception) when (exception.Number is 51003 or 51004)
        {
            throw new InvalidOperationException(
                $"Ownership validation failed for temporary database '{DatabaseName}'. It was not dropped.",
                exception);
        }
    }

    private void VerifyGeneratedDatabaseIdentity()
    {
        string expectedName = $"{DatabasePrefix}{_runId:N}";
        bool isProtectedName =
            string.Equals(expectedName, DevelopmentDatabaseName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(expectedName, ExistingTestDatabaseName, StringComparison.OrdinalIgnoreCase);

        if (isProtectedName ||
            !string.Equals(DatabaseName, expectedName, StringComparison.Ordinal) ||
            !Guid.TryParseExact(expectedName[DatabasePrefix.Length..], "N", out Guid parsedRunId) ||
            parsedRunId != _runId)
        {
            throw new InvalidOperationException(
                "Generated test database identity could not be verified; no database was dropped.");
        }
    }
}
