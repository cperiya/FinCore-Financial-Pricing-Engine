using System.Data;
using System.Globalization;
using System.Text.Json;
using FinCore.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Benchmarks;

internal sealed class BenchmarkDatabaseFixture : IAsyncDisposable
{
    private const string ConnectionStringEnvironmentVariable =
        "FINCORE_BENCHMARK_SQLSERVER_CONNECTION_STRING";
    private const string OwnershipPropertyName = "FinCoreBenchmarkRunOwner";
    private const string DatabasePrefix = "FinCoreBenchmarkRun_";
    private const string ProtectedDatabaseNames = "FinCore, FinCoreIntegrationTests";
    private const string MasterLockResourcePrefix = "FinCoreBenchmarkRun:";
    private const string DatabaseLeaseResourcePrefix = "FinCoreBenchmarkLease:";

    private readonly Guid _runId = Guid.NewGuid();
    private SqlConnection? _masterConnection;
    private SqlConnection? _databaseLeaseConnection;
    private string? _benchmarkConnectionString;
    private string? _manifestPath;
    private bool _databaseCreated;
    private bool _ownershipMarkerRecorded;
    private bool _manifestCreated;

    public string DatabaseName => $"{DatabasePrefix}{_runId:N}";

    public string BenchmarkConnectionString =>
        _benchmarkConnectionString ??
        throw new InvalidOperationException("The benchmark database has not been initialized.");

    public SqlServerEnvironment ServerEnvironment { get; private set; } =
        new("unknown", "unknown", "unknown");

    /// <summary>
    /// Reads the compatibility level of this fixture's ownership-verified database.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the SQL metadata query.</param>
    /// <returns>The database compatibility level reported by SQL Server.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the compatibility level cannot be read from the run-owned database.
    /// </exception>
    public async Task<int> ReadDatabaseCompatibilityLevelAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(BenchmarkConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT compatibility_level
            FROM sys.databases
            WHERE database_id = DB_ID();
            """;

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not byte and not short and not int)
        {
            throw new InvalidOperationException(
                "Could not verify the run-owned benchmark database compatibility level.");
        }

        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    internal static async Task ValidateWorkerDatabaseAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                "The benchmark worker database connection is invalid; its value was not printed.");
        }

        Guid runId = VerifyDatabaseName(builder.InitialCatalog);
        if (!IsLocalSqlServerEndpoint(builder.DataSource) ||
            !builder.Encrypt ||
            !builder.TrustServerCertificate ||
            builder.IntegratedSecurity ||
            string.IsNullOrWhiteSpace(builder.UserID) ||
            !builder.Pooling)
        {
            throw new InvalidOperationException(
                "Benchmark workers require the encrypted local SQL Server 2022 connection " +
                "and an exact run-owned FinCoreBenchmarkRun_<GUID> database.");
        }

        await using SqlConnection connection = new(builder.ConnectionString);
        await OpenSqlConnectionAsync(connection, "the run-owned benchmark database");
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                DB_NAME(),
                CONVERT(nvarchar(32), value)
            FROM sys.extended_properties
            WHERE class = 0
              AND name = @PropertyName;
            """;
        command.Parameters.Add("@PropertyName", SqlDbType.NVarChar, 128)
            .Value = OwnershipPropertyName;

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            !string.Equals(
                reader.GetString(0),
                builder.InitialCatalog,
                StringComparison.Ordinal) ||
            !string.Equals(
                reader.GetString(1),
                runId.ToString("N"),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The benchmark worker could not verify the run-owned database marker; " +
                "no database operation was performed.");
        }
    }

    public async Task InitializeAsync()
    {
        if (_masterConnection is not null)
        {
            throw new InvalidOperationException("This benchmark database fixture is already initialized.");
        }

        string configuredConnectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ?? string.Empty;
        SqlConnectionStringBuilder connectionBuilder = ParseAndValidateConnection(configuredConnectionString);
        string dataSource = connectionBuilder.DataSource;

        connectionBuilder.InitialCatalog = "master";
        connectionBuilder.Pooling = false;
        connectionBuilder.ApplicationName = $"FinCore.Benchmarks.Lifecycle/{_runId:N}";
        _masterConnection = new SqlConnection(connectionBuilder.ConnectionString);
        await OpenSqlConnectionAsync(_masterConnection, "the local SQL Server master database");
        ServerEnvironment = await ValidateLocalSqlServerAsync(_masterConnection);
        await AcquireApplicationLockAsync(
            _masterConnection,
            $"{MasterLockResourcePrefix}{DatabaseName}",
            "Exclusive");

        _manifestPath = GetManifestPath(DatabaseName);
        WriteManifest(new BenchmarkRunManifest(
            DatabaseName,
            _runId.ToString("N"),
            dataSource,
            Environment.ProcessId,
            DateTimeOffset.UtcNow,
            "Creating"));

        VerifyGeneratedDatabaseIdentity(DatabaseName, _runId);
        await CreateDatabaseAsync(_masterConnection, DatabaseName);
        _databaseCreated = true;

        connectionBuilder.InitialCatalog = DatabaseName;
        connectionBuilder.Pooling = true;
        connectionBuilder.ApplicationName = $"FinCore.Benchmarks.Worker/{_runId:N}";
        _benchmarkConnectionString = connectionBuilder.ConnectionString;

        await RecordOwnershipMarkerAsync(_benchmarkConnectionString, _runId);
        _ownershipMarkerRecorded = true;

        connectionBuilder.Pooling = false;
        connectionBuilder.ApplicationName = $"FinCore.Benchmarks.Lease/{_runId:N}";
        _databaseLeaseConnection = new SqlConnection(connectionBuilder.ConnectionString);
        await OpenSqlConnectionAsync(_databaseLeaseConnection, "the generated benchmark database");
        await AcquireApplicationLockAsync(
            _databaseLeaseConnection,
            $"{DatabaseLeaseResourcePrefix}{_runId:N}",
            "Shared");

        await ApplyMigrationsAsync(_benchmarkConnectionString);
        WriteManifest(new BenchmarkRunManifest(
            DatabaseName,
            _runId.ToString("N"),
            dataSource,
            Environment.ProcessId,
            DateTimeOffset.UtcNow,
            "Ready"));
    }

    public async ValueTask DisposeAsync()
    {
        if (!_databaseCreated)
        {
            if (_masterConnection is not null)
            {
                await _masterConnection.DisposeAsync();
                _masterConnection = null;
            }

            return;
        }

        if (!_ownershipMarkerRecorded)
        {
            throw new InvalidOperationException(
                $"Database '{DatabaseName}' may exist but its owner marker was not recorded. " +
                "The database was not dropped.");
        }

        VerifyGeneratedDatabaseIdentity(DatabaseName, _runId);
        SqlConnection masterConnection = _masterConnection ??
            throw new InvalidOperationException(
                $"The lifecycle lock for '{DatabaseName}' is unavailable. The database was not dropped.");

        if (_databaseLeaseConnection is not null)
        {
            await _databaseLeaseConnection.DisposeAsync();
            _databaseLeaseConnection = null;
        }

        await DropOwnedDatabaseAsync(masterConnection, DatabaseName, _runId);
        _databaseCreated = false;
        _ownershipMarkerRecorded = false;
        DeleteManifestIfPresent(_manifestPath);

        await masterConnection.DisposeAsync();
        _masterConnection = null;
    }

    public static async Task CleanupOwnedDatabaseAsync(string databaseName)
    {
        Guid runId = VerifyDatabaseName(databaseName);
        string manifestPath = GetManifestPath(databaseName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"No run manifest exists for '{databaseName}'. Refusing cleanup without its ownership record.");
        }

        BenchmarkRunManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BenchmarkRunManifest>(
                await File.ReadAllTextAsync(manifestPath)) ??
                throw new JsonException();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"The ownership manifest for '{databaseName}' is invalid. The database was not dropped.");
        }

        if (!string.Equals(manifest.DatabaseName, databaseName, StringComparison.Ordinal) ||
            !string.Equals(manifest.OwnerId, runId.ToString("N"), StringComparison.Ordinal) ||
            !IsLocalSqlServerEndpoint(manifest.DataSource))
        {
            throw new InvalidOperationException(
                $"The ownership manifest does not match '{databaseName}'. The database was not dropped.");
        }

        SqlConnectionStringBuilder connectionBuilder = ParseAndValidateConnection(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ?? string.Empty);
        connectionBuilder.InitialCatalog = "master";
        connectionBuilder.Pooling = false;
        connectionBuilder.ApplicationName = $"FinCore.Benchmarks.Cleanup/{runId:N}";

        await using SqlConnection masterConnection = new(connectionBuilder.ConnectionString);
        await OpenSqlConnectionAsync(masterConnection, "the local SQL Server master database");
        await ValidateLocalSqlServerAsync(masterConnection);
        await AcquireApplicationLockAsync(
            masterConnection,
            $"{MasterLockResourcePrefix}{databaseName}",
            "Exclusive");

        bool databaseExists = await DatabaseExistsAsync(masterConnection, databaseName);
        if (!databaseExists)
        {
            DeleteManifestIfPresent(manifestPath);
            Console.WriteLine($"No database named '{databaseName}' remains; its stale run manifest was removed.");
            return;
        }

        connectionBuilder.InitialCatalog = databaseName;
        connectionBuilder.ApplicationName = $"FinCore.Benchmarks.CleanupLease/{runId:N}";
        await using SqlConnection databaseLease = new(connectionBuilder.ConnectionString);
        await OpenSqlConnectionAsync(databaseLease, "the generated benchmark database");
        await AcquireApplicationLockAsync(
            databaseLease,
            $"{DatabaseLeaseResourcePrefix}{runId:N}",
            "Exclusive");
        await VerifyOwnershipMarkerAsync(masterConnection, databaseName, runId);

        await databaseLease.CloseAsync();
        await DropOwnedDatabaseAsync(masterConnection, databaseName, runId);
        DeleteManifestIfPresent(manifestPath);
        Console.WriteLine($"Safely removed run-owned database '{databaseName}'.");
    }

    public static async Task<ProtectedDatabaseSnapshot> ReadProtectedDatabaseSnapshotAsync()
    {
        SqlConnectionStringBuilder connectionBuilder = ParseAndValidateConnection(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ?? string.Empty);
        connectionBuilder.InitialCatalog = "master";
        connectionBuilder.Pooling = false;
        connectionBuilder.ApplicationName = "FinCore.Benchmarks.ReadOnlyProtectedState";

        await using SqlConnection masterConnection = new(connectionBuilder.ConnectionString);
        await OpenSqlConnectionAsync(masterConnection, "the local SQL Server master database");
        await ValidateLocalSqlServerAsync(masterConnection);

        await using SqlCommand command = masterConnection.CreateCommand();
        command.CommandText = """
            IF DB_ID(N'FinCore') IS NULL OR DB_ID(N'FinCoreIntegrationTests') IS NULL
                THROW 51005, 'A protected FinCore database is missing.', 1;
            SELECT
                DB_NAME(),
                (SELECT COUNT_BIG(*) FROM [FinCore].dbo.Portfolios),
                (SELECT COUNT_BIG(*) FROM [FinCore].dbo.Instruments),
                (SELECT COUNT_BIG(*) FROM [FinCore].dbo.Trades),
                (SELECT COUNT_BIG(*) FROM [FinCore].dbo.MarketPrices),
                (SELECT COUNT_BIG(*) FROM [FinCoreIntegrationTests].dbo.Portfolios),
                (SELECT COUNT_BIG(*) FROM [FinCoreIntegrationTests].dbo.Instruments),
                (SELECT COUNT_BIG(*) FROM [FinCoreIntegrationTests].dbo.Trades),
                (SELECT COUNT_BIG(*) FROM [FinCoreIntegrationTests].dbo.MarketPrices);
            """;

        try
        {
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync() ||
                !string.Equals(reader.GetString(0), "master", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The protected-database check did not remain in master.");
            }

            return new ProtectedDatabaseSnapshot(
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8));
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                $"Could not read the protected database row-count snapshot (SQL error {exception.Number}); " +
                "no protected database was modified.");
        }
    }

    public static async Task<IReadOnlyList<string>> ReadOwnedDatabaseNamesAsync()
    {
        SqlConnectionStringBuilder connectionBuilder = ParseAndValidateConnection(
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ?? string.Empty);
        connectionBuilder.InitialCatalog = "master";
        connectionBuilder.Pooling = false;
        connectionBuilder.ApplicationName = "FinCore.Benchmarks.ReadOnlyOwnedState";

        await using SqlConnection masterConnection = new(connectionBuilder.ConnectionString);
        await OpenSqlConnectionAsync(masterConnection, "the local SQL Server master database");
        await ValidateLocalSqlServerAsync(masterConnection);

        await using SqlCommand command = masterConnection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sys.databases
            WHERE name LIKE @DatabasePattern
            ORDER BY name;
            """;
        command.Parameters.Add("@DatabasePattern", SqlDbType.NVarChar, 128)
            .Value = $"{DatabasePrefix}%";

        List<string> databaseNames = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            databaseNames.Add(reader.GetString(0));
        }

        return databaseNames;
    }

    private static SqlConnectionStringBuilder ParseAndValidateConnection(string configuredConnectionString)
    {
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringEnvironmentVariable} for SQL-only benchmarks. " +
                "Connection values are never printed.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(configuredConnectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} is invalid. Its value was not printed.");
        }

        if (!string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase) ||
            !IsLocalSqlServerEndpoint(builder.DataSource))
        {
            throw new InvalidOperationException(
                "Benchmarks require Data Source 127.0.0.1,1433 and Initial Catalog master. " +
                $"Only a generated {DatabasePrefix}<GUID> database may be written.");
        }

        if (!builder.Encrypt ||
            !builder.TrustServerCertificate ||
            builder.IntegratedSecurity ||
            string.IsNullOrWhiteSpace(builder.UserID) ||
            !builder.Pooling)
        {
            throw new InvalidOperationException(
                "Benchmarks require encrypted SQL authentication, local certificate trust, and connection pooling.");
        }

        return builder;
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

    private static async Task OpenSqlConnectionAsync(SqlConnection connection, string purpose)
    {
        try
        {
            await connection.OpenAsync();
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                $"Could not open {purpose} (SQL error {exception.Number}). " +
                "Connection values were not logged.");
        }
    }

    private static async Task<SqlServerEnvironment> ValidateLocalSqlServerAsync(SqlConnection connection)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                DB_NAME(),
                COALESCE(CONVERT(nvarchar(32), SERVERPROPERTY('ProductMajorVersion')), N''),
                COALESCE(CONVERT(nvarchar(128), SERVERPROPERTY('Edition')), N''),
                COALESCE(CONVERT(nvarchar(32), SERVERPROPERTY('ProductVersion')), N''),
                IS_SRVROLEMEMBER(N'sysadmin');
            """;

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                "Could not verify the local SQL Server; no benchmark database was created.");
        }

        string currentDatabase = reader.GetString(0);
        string majorVersion = reader.GetString(1);
        string edition = reader.GetString(2);
        string productVersion = reader.GetString(3);
        int isSysAdmin = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);

        if (!string.Equals(currentDatabase, "master", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(majorVersion, "16", StringComparison.Ordinal) ||
            !edition.Contains("Developer Edition", StringComparison.OrdinalIgnoreCase) ||
            isSysAdmin != 1)
        {
            throw new InvalidOperationException(
                "Refusing benchmark database operations: the endpoint must be a sysadmin-enabled " +
                "SQL Server 2022 Developer Edition instance at 127.0.0.1,1433.");
        }

        return new SqlServerEnvironment(productVersion, edition, majorVersion);
    }

    private static async Task AcquireApplicationLockAsync(
        SqlConnection connection,
        string resource,
        string lockMode)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = @ResourceName,
                @LockMode = @LockModeName,
                @LockOwner = N'Session',
                @LockTimeout = 0,
                @DbPrincipal = N'public';
            SELECT @LockResult;
            """;
        command.Parameters.Add("@ResourceName", SqlDbType.NVarChar, 255).Value = resource;
        command.Parameters.Add("@LockModeName", SqlDbType.NVarChar, 32).Value = lockMode;

        int lockResult;
        try
        {
            object? result = await command.ExecuteScalarAsync();
            lockResult = result is int value ? value : int.MinValue;
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                $"Could not acquire the SQL benchmark ownership lock (SQL error {exception.Number}).");
        }

        if (lockResult < 0)
        {
            throw new InvalidOperationException(
                $"SQL Server refused the benchmark ownership lock (result {lockResult}); " +
                "the database may be active or ownership cannot be proven.");
        }
    }

    private static async Task CreateDatabaseAsync(SqlConnection masterConnection, string databaseName)
    {
        await using SqlCommand command = masterConnection.CreateCommand();
        command.CommandText = """
            IF @DatabaseName IN (N'FinCore', N'FinCoreIntegrationTests')
                THROW 51000, 'Refusing a protected FinCore database name.', 1;
            IF DB_ID(@DatabaseName) IS NOT NULL
                THROW 51001, 'The generated benchmark database already exists.', 1;
            DECLARE @CreateDatabase nvarchar(max) =
                N'CREATE DATABASE ' + QUOTENAME(@DatabaseName);
            EXEC sys.sp_executesql @CreateDatabase;
            """;
        command.Parameters.Add("@DatabaseName", SqlDbType.NVarChar, 128).Value = databaseName;

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                $"Could not create the unique benchmark database (SQL error {exception.Number}).");
        }
    }

    private static async Task RecordOwnershipMarkerAsync(string connectionString, Guid runId)
    {
        await using SqlConnection connection = new(connectionString);
        await OpenSqlConnectionAsync(connection, "the generated benchmark database");
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = """
            EXEC sys.sp_addextendedproperty
                @name = @PropertyName,
                @value = @OwnerId;
            """;
        command.Parameters.Add("@PropertyName", SqlDbType.NVarChar, 128).Value = OwnershipPropertyName;
        command.Parameters.Add("@OwnerId", SqlDbType.NVarChar, 32).Value = runId.ToString("N");

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                $"Could not record the benchmark ownership marker (SQL error {exception.Number}). " +
                "The database was not dropped because ownership could not be verified.");
        }
    }

    private static async Task ApplyMigrationsAsync(string connectionString)
    {
        DbContextOptions<FinCoreDbContext> options = new DbContextOptionsBuilder<FinCoreDbContext>()
            .UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly(
                    typeof(FinCoreDbContext).Assembly.GetName().Name))
            .Options;

        await using FinCoreDbContext dbContext = new(options);
        try
        {
            await dbContext.Database.MigrateAsync();
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                $"Could not apply the existing FinCore migration to the owned benchmark database " +
                $"(SQL error {exception.Number}).");
        }
    }

    private static async Task<bool> DatabaseExistsAsync(SqlConnection masterConnection, string databaseName)
    {
        await using SqlCommand command = masterConnection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@DatabaseName) IS NULL THEN 0 ELSE 1 END;";
        command.Parameters.Add("@DatabaseName", SqlDbType.NVarChar, 128).Value = databaseName;
        object? result = await command.ExecuteScalarAsync();
        return result is int exists && exists == 1;
    }

    private static async Task VerifyOwnershipMarkerAsync(
        SqlConnection masterConnection,
        string databaseName,
        Guid runId)
    {
        VerifyGeneratedDatabaseIdentity(databaseName, runId);

        await using SqlCommand command = masterConnection.CreateCommand();
        command.CommandText = """
            DECLARE @DatabaseName sysname = @RequestedDatabaseName;
            IF DB_ID(@DatabaseName) IS NULL
                THROW 51003, 'The owned benchmark database is missing.', 1;

            DECLARE @VerifyOwner nvarchar(max) =
                N'IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@DatabaseName) +
                N'.sys.extended_properties WHERE class = 0 AND name = @PropertyName ' +
                N'AND CONVERT(nvarchar(128), value) = @OwnerId) ' +
                N'THROW 51004, ''Ownership marker does not match this benchmark run.'', 1;';
            EXEC sys.sp_executesql
                @VerifyOwner,
                N'@PropertyName nvarchar(128), @OwnerId nvarchar(32)',
                @PropertyName = @ExpectedPropertyName,
                @OwnerId = @ExpectedOwnerId;
            """;
        command.Parameters.Add("@RequestedDatabaseName", SqlDbType.NVarChar, 128).Value = databaseName;
        command.Parameters.Add("@ExpectedPropertyName", SqlDbType.NVarChar, 128).Value = OwnershipPropertyName;
        command.Parameters.Add("@ExpectedOwnerId", SqlDbType.NVarChar, 32).Value = runId.ToString("N");

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                $"Ownership verification failed for '{databaseName}' (SQL error {exception.Number}). " +
                "The database was not dropped.");
        }
    }

    private static async Task DropOwnedDatabaseAsync(
        SqlConnection masterConnection,
        string databaseName,
        Guid runId)
    {
        VerifyGeneratedDatabaseIdentity(databaseName, runId);
        await VerifyOwnershipMarkerAsync(masterConnection, databaseName, runId);

        await using SqlCommand command = masterConnection.CreateCommand();
        command.CommandText = """
            DECLARE @DatabaseName sysname = @RequestedDatabaseName;
            DECLARE @DropDatabase nvarchar(max) =
                N'ALTER DATABASE ' + QUOTENAME(@DatabaseName) +
                N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' +
                QUOTENAME(@DatabaseName);
            EXEC sys.sp_executesql @DropDatabase;
            """;
        command.Parameters.Add("@RequestedDatabaseName", SqlDbType.NVarChar, 128).Value = databaseName;

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException exception)
        {
            throw new InvalidOperationException(
                $"Could not drop the verified run-owned database '{databaseName}' " +
                $"(SQL error {exception.Number}); it was not treated as cleaned up.");
        }
    }

    private static string GetManifestPath(string databaseName)
    {
        string? repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            throw new InvalidOperationException(
                "Could not locate FinCore.slnx; benchmark run ownership metadata was not written.");
        }

        string directory = Path.Combine(
            repositoryRoot,
            "BenchmarkDotNet.Artifacts",
            "FinCoreBenchmarkRuns");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{databaseName}.json");
    }

    private static string? FindRepositoryRoot()
    {
        foreach (string startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(startPath));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FinCore.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private void WriteManifest(BenchmarkRunManifest manifest)
    {
        string manifestPath = _manifestPath ?? throw new InvalidOperationException(
            "The run manifest path has not been initialized.");

        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        if (!_manifestCreated)
        {
            try
            {
                using FileStream stream = new(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using StreamWriter writer = new(stream);
                writer.Write(json);
                _manifestCreated = true;
            }
            catch (IOException)
            {
                throw new InvalidOperationException(
                    $"A benchmark ownership manifest already exists for '{DatabaseName}'. " +
                    "The database was not created.");
            }

            return;
        }

        File.WriteAllText(manifestPath, json);
    }

    private static void DeleteManifestIfPresent(string? manifestPath)
    {
        if (manifestPath is not null && File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
        }
    }

    private static Guid VerifyDatabaseName(string databaseName)
    {
        if (databaseName is null ||
            !databaseName.StartsWith(DatabasePrefix, StringComparison.Ordinal) ||
            databaseName.Length != DatabasePrefix.Length + 32 ||
            !Guid.TryParseExact(databaseName[DatabasePrefix.Length..], "N", out Guid runId) ||
            !string.Equals(databaseName, $"{DatabasePrefix}{runId:N}", StringComparison.Ordinal) ||
            string.Equals(databaseName, "FinCore", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(databaseName, "FinCoreIntegrationTests", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cleanup accepts only an exact {DatabasePrefix}<32-character-GUID> database name.");
        }

        return runId;
    }

    private static void VerifyGeneratedDatabaseIdentity(string databaseName, Guid runId)
    {
        Guid parsedRunId = VerifyDatabaseName(databaseName);
        if (parsedRunId != runId)
        {
            throw new InvalidOperationException(
                "Generated benchmark database identity does not match the current run; no database was dropped.");
        }
    }

    private sealed record BenchmarkRunManifest(
        string DatabaseName,
        string OwnerId,
        string DataSource,
        int ProcessId,
        DateTimeOffset StartedAtUtc,
        string Status);
}

internal sealed record ProtectedDatabaseSnapshot(
    long FinCorePortfolios,
    long FinCoreInstruments,
    long FinCoreTrades,
    long FinCoreMarketPrices,
    long IntegrationPortfolios,
    long IntegrationInstruments,
    long IntegrationTrades,
    long IntegrationMarketPrices);

internal sealed record SqlServerEnvironment(string ProductVersion, string Edition, string MajorVersion)
{
    public string ToSummary()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"SQL Server {ProductVersion} ({Edition}, major {MajorVersion})");
    }
}
