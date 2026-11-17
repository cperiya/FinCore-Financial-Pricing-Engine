using System.Data.Common;
using FinCore.Application.Persistence;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.DependencyInjection;
using FinCore.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.IntegrationTests;

/// <summary>
/// Verifies EF Core persistence behavior against a separate SQL Server integration-test database.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SqlServerPersistenceTests
{
    private const string InitialMigrationId = "20261115200739_InitialPortfolioPersistence";

    private readonly SqlServerTestDatabaseFixture _fixture;

    /// <summary>
    /// Initializes persistence tests using the collection's run-owned database fixture.
    /// </summary>
    /// <param name="fixture">Fixture that creates, migrates, and cleans up the isolated test database.</param>
    public SqlServerPersistenceTests(SqlServerTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies domain entities are stored and reconstructed through a later DbContext.
    /// </summary>
    [SqlServerFact]
    public async Task Repositories_RoundTripPortfolioInstrumentTradeAndMarketPrice()
    {
        Guid portfolioId = Guid.NewGuid();
        Guid instrumentId = Guid.NewGuid();
        Guid tradeId = Guid.NewGuid();
        Guid marketPriceId = Guid.NewGuid();
        DateTimeOffset timestamp = new(2025, 2, 3, 10, 30, 0, TimeSpan.Zero);
        Portfolio portfolio = new(portfolioId, "  Growth  ", "USD", timestamp);
        string symbol = CreateUniqueSymbol('R');
        Instrument instrument = new(instrumentId, $"  {symbol.ToLowerInvariant()} ", "Microsoft", "USD",
            InstrumentType.Equity);
        Trade trade = new(
            tradeId,
            portfolioId,
            instrumentId,
            TradeSide.Buy,
            12.3456789012m,
            345.1234567890m,
            timestamp);
        MarketPrice marketPrice = new(marketPriceId, instrumentId, 347.9876543210m, "USD", timestamp);

        await using ServiceProvider provider = CreateServiceProvider();
        await using (AsyncServiceScope writeScope = provider.CreateAsyncScope())
        {
            await writeScope.ServiceProvider.GetRequiredService<IPortfolioRepository>()
                .AddAsync(portfolio, CancellationToken.None);
            await writeScope.ServiceProvider.GetRequiredService<IInstrumentRepository>()
                .AddAsync(instrument, CancellationToken.None);
            await writeScope.ServiceProvider.GetRequiredService<ITradeRepository>()
                .AddAsync(trade, CancellationToken.None);
            await writeScope.ServiceProvider.GetRequiredService<IMarketPriceRepository>()
                .AddAsync(marketPrice, CancellationToken.None);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(CancellationToken.None);
        }

        await using AsyncServiceScope readScope = provider.CreateAsyncScope();
        Portfolio? storedPortfolio = await readScope.ServiceProvider
            .GetRequiredService<IPortfolioRepository>()
            .GetByIdAsync(portfolioId, CancellationToken.None);
        Instrument? storedInstrument = await readScope.ServiceProvider
            .GetRequiredService<IInstrumentRepository>()
            .GetByIdAsync(instrumentId, CancellationToken.None);
        IReadOnlyList<Trade> storedTrades = await readScope.ServiceProvider
            .GetRequiredService<ITradeRepository>()
            .GetByPortfolioAndInstrumentAsync(portfolioId, instrumentId, CancellationToken.None);
        MarketPrice? storedMarketPrice = await readScope.ServiceProvider
            .GetRequiredService<IMarketPriceRepository>()
            .GetLatestByInstrumentIdAsync(instrumentId, CancellationToken.None);

        Assert.Equal("Growth", storedPortfolio?.Name);
        Assert.Equal(symbol, storedInstrument?.Symbol);
        Assert.Equal(12.3456789012m, Assert.Single(storedTrades).Quantity);
        Assert.Equal(345.1234567890m, storedTrades[0].UnitPrice);
        Assert.Equal(347.9876543210m, storedMarketPrice?.Price);
        Assert.Equal(TimeSpan.Zero, storedMarketPrice?.AsOfUtc.Offset);
    }

    /// <summary>
    /// Verifies latest-price lookup chooses the greatest observation timestamp and returns null when absent.
    /// </summary>
    [SqlServerFact]
    public async Task MarketPriceRepository_ReturnsLatestPriceOrNullWhenMissing()
    {
        Guid instrumentId = Guid.NewGuid();
        DateTimeOffset earlier = new(2025, 2, 3, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset later = earlier.AddMinutes(1);

        await using ServiceProvider provider = CreateServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>()
            .AddAsync(
                new Instrument(
                    instrumentId,
                    CreateUniqueSymbol('L'),
                    "Synthetic Test Instrument",
                    "USD",
                    InstrumentType.Equity),
                CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IMarketPriceRepository>()
            .AddAsync(new MarketPrice(Guid.NewGuid(), instrumentId, 10m, "USD", earlier), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IMarketPriceRepository>()
            .AddAsync(new MarketPrice(Guid.NewGuid(), instrumentId, 11m, "USD", later), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync(CancellationToken.None);

        IMarketPriceRepository repository = scope.ServiceProvider.GetRequiredService<IMarketPriceRepository>();
        MarketPrice? latest = await repository.GetLatestByInstrumentIdAsync(instrumentId, CancellationToken.None);
        MarketPrice? absent = await repository.GetLatestByInstrumentIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(11m, latest?.Price);
        Assert.Equal(later, latest?.AsOfUtc);
        Assert.Null(absent);
    }

    /// <summary>
    /// Verifies financial values outside decimal(28,10) are rejected before SQL Server can round them.
    /// </summary>
    [SqlServerFact]
    public async Task DbContext_WhenFinancialValueExceedsColumnPrecision_RejectsItBeforeSave()
    {
        Guid portfolioId = Guid.NewGuid();
        Guid tradeInstrumentId = Guid.NewGuid();
        Guid priceInstrumentId = Guid.NewGuid();
        DateTimeOffset timestamp = new(2025, 2, 3, 10, 30, 0, TimeSpan.Zero);

        await using ServiceProvider provider = CreateServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        FinCoreDbContext dbContext = scope.ServiceProvider.GetRequiredService<FinCoreDbContext>();

        await scope.ServiceProvider.GetRequiredService<IPortfolioRepository>()
            .AddAsync(new Portfolio(portfolioId, "Precision Probe", "USD", timestamp), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>()
            .AddAsync(
                new Instrument(
                    tradeInstrumentId,
                    CreateUniqueSymbol('T'),
                    "Synthetic Trade Instrument",
                    "USD",
                    InstrumentType.Equity),
                CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ITradeRepository>()
            .AddAsync(
                new Trade(
                    Guid.NewGuid(),
                    portfolioId,
                    tradeInstrumentId,
                    TradeSide.Buy,
                    1.00000000001m,
                    1m,
                    timestamp),
                CancellationToken.None);

        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.SaveChangesAsync(CancellationToken.None));

        dbContext.ChangeTracker.Clear();
        await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>()
            .AddAsync(
                new Instrument(
                    priceInstrumentId,
                    CreateUniqueSymbol('P'),
                    "Synthetic Price Instrument",
                    "USD",
                    InstrumentType.Equity),
                CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IMarketPriceRepository>()
            .AddAsync(
                new MarketPrice(Guid.NewGuid(), priceInstrumentId, 1.00000000001m, "USD", timestamp),
                CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>
    /// Verifies a failed operation rolls back changes already saved within its serializable transaction.
    /// </summary>
    [SqlServerFact]
    public async Task UnitOfWork_WhenOperationFails_RollsBackPersistedChanges()
    {
        Guid portfolioId = Guid.NewGuid();
        Portfolio portfolio = new(portfolioId, "Rollback Probe", "USD", DateTimeOffset.UtcNow);

        await using ServiceProvider provider = CreateServiceProvider();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            IPortfolioRepository repository = scope.ServiceProvider.GetRequiredService<IPortfolioRepository>();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => unitOfWork.ExecuteInTransactionAsync(
                    async cancellationToken =>
                    {
                        await repository.AddAsync(portfolio, cancellationToken);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        return await Task.FromException<bool>(
                            new InvalidOperationException("Synthetic rollback test."));
                    },
                    CancellationToken.None));
        }

        await using AsyncServiceScope verificationScope = provider.CreateAsyncScope();
        Portfolio? persistedPortfolio = await verificationScope.ServiceProvider
            .GetRequiredService<IPortfolioRepository>()
            .GetByIdAsync(portfolioId, CancellationToken.None);

        Assert.Null(persistedPortfolio);
    }

    /// <summary>
    /// Verifies the migration history, indexes, foreign keys, and check constraints in the migrated database.
    /// </summary>
    [SqlServerFact]
    public async Task Migration_AppliesExpectedHistoryAndSchemaObjects()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        FinCoreDbContext dbContext = scope.ServiceProvider.GetRequiredService<FinCoreDbContext>();

        string[] appliedMigrations = (await dbContext.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Contains(InitialMigrationId, appliedMigrations);

        int historyRows = await ExecuteScalarIntAsync(
            dbContext,
            $"SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = '{InitialMigrationId}'");
        Assert.Equal(1, historyRows);

        Dictionary<string, string> foreignKeys = await ReadNameAndActionAsync(
            dbContext,
            """
            SELECT name, delete_referential_action_desc
            FROM sys.foreign_keys
            WHERE name IN (
                N'FK_MarketPrices_Instruments_InstrumentId',
                N'FK_Trades_Instruments_InstrumentId',
                N'FK_Trades_Portfolios_PortfolioId');
            """);
        Assert.Equal(3, foreignKeys.Count);
        Assert.All(foreignKeys.Values, action => Assert.Equal("NO_ACTION", action));

        Dictionary<string, string> checkConstraints = await ReadNameAndActionAsync(
            dbContext,
            """
            SELECT name, definition
            FROM sys.check_constraints
            WHERE parent_object_id IN (
                OBJECT_ID(N'dbo.Instruments'),
                OBJECT_ID(N'dbo.Portfolios'),
                OBJECT_ID(N'dbo.Trades'),
                OBJECT_ID(N'dbo.MarketPrices'));
            """);
        string[] expectedCheckConstraints =
        [
            "CK_Instruments_Currency_USD",
            "CK_Instruments_InstrumentType_Equity",
            "CK_MarketPrices_AsOfUtc_UTC",
            "CK_MarketPrices_Currency_USD",
            "CK_MarketPrices_Price_Positive",
            "CK_Portfolios_BaseCurrency_USD",
            "CK_Portfolios_CreatedAt_UTC",
            "CK_Trades_ExecutedAt_UTC",
            "CK_Trades_Quantity_Positive",
            "CK_Trades_Side_Supported",
            "CK_Trades_UnitPrice_Positive"
        ];
        Assert.Equal(
            expectedCheckConstraints.OrderBy(name => name),
            checkConstraints.Keys.OrderBy(name => name));
        Assert.Contains("USD", checkConstraints["CK_Instruments_Currency_USD"]);
        Assert.Contains("1", checkConstraints["CK_Instruments_InstrumentType_Equity"]);
        Assert.Contains("0", checkConstraints["CK_MarketPrices_AsOfUtc_UTC"]);
        Assert.Contains("0", checkConstraints["CK_MarketPrices_Price_Positive"]);
        Assert.Contains("USD", checkConstraints["CK_Portfolios_BaseCurrency_USD"]);
        Assert.Contains("0", checkConstraints["CK_Portfolios_CreatedAt_UTC"]);
        Assert.Contains("0", checkConstraints["CK_Trades_ExecutedAt_UTC"]);
        Assert.Contains("0", checkConstraints["CK_Trades_Quantity_Positive"]);
        Assert.Contains("1", checkConstraints["CK_Trades_Side_Supported"]);
        Assert.Contains("2", checkConstraints["CK_Trades_Side_Supported"]);
        Assert.Contains("0", checkConstraints["CK_Trades_UnitPrice_Positive"]);

        List<IndexColumn> indexColumns = await ReadIndexColumnsAsync(dbContext);
        AssertIndexColumns(indexColumns, "PK_Portfolios", true, ("Id", false));
        AssertIndexColumns(indexColumns, "PK_Instruments", true, ("Id", false));
        AssertIndexColumns(indexColumns, "PK_Trades", true, ("Id", false));
        AssertIndexColumns(indexColumns, "PK_MarketPrices", true, ("Id", false));
        AssertIndexColumns(indexColumns, "UX_Instruments_Symbol", true, ("Symbol", false));
        AssertIndexColumns(indexColumns, "IX_Trades_InstrumentId", false, ("InstrumentId", false));
        AssertIndexColumns(
            indexColumns,
            "IX_Trades_Portfolio_Instrument_Execution",
            false,
            ("PortfolioId", false),
            ("InstrumentId", false),
            ("ExecutedAt", false),
            ("Side", false),
            ("Id", false));
        AssertIndexColumns(
            indexColumns,
            "IX_MarketPrices_Instrument_Latest",
            false,
            ("InstrumentId", false),
            ("AsOfUtc", true),
            ("Id", true));
    }

    /// <summary>
    /// Verifies symbols that normalize to the same value are rejected by the unique database index.
    /// </summary>
    [SqlServerFact]
    public async Task InstrumentRepository_RejectsDuplicateNormalizedSymbols()
    {
        Guid firstId = Guid.NewGuid();
        Guid duplicateId = Guid.NewGuid();
        string symbol = CreateUniqueSymbol('D');
        Instrument first = new(firstId, $"  {symbol.ToLowerInvariant()}  ", "First Synthetic Instrument",
            "USD", InstrumentType.Equity);
        Instrument duplicate = new(duplicateId, symbol, "Duplicate Synthetic Instrument",
            "USD", InstrumentType.Equity);

        Assert.Equal(symbol, first.Symbol);
        Assert.Equal(first.Symbol, duplicate.Symbol);

        await using ServiceProvider provider = CreateServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IInstrumentRepository repository = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await repository.AddAsync(first, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        await repository.AddAsync(duplicate, CancellationToken.None);

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => unitOfWork.SaveChangesAsync(CancellationToken.None));
        SqlException sqlException = Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains(sqlException.Number, new[] { 2601, 2627 });
    }

    /// <summary>
    /// Verifies database check constraints reject invalid values even when writes bypass Domain constructors.
    /// </summary>
    [SqlServerFact]
    public async Task DatabaseCheckConstraints_RejectInvalidInstrumentCurrencyAndMarketPrice()
    {
        Guid instrumentId = Guid.NewGuid();
        await using ServiceProvider provider = CreateServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        FinCoreDbContext dbContext = scope.ServiceProvider.GetRequiredService<FinCoreDbContext>();

        dbContext.Instruments.Add(
            new Instrument(
                instrumentId,
                CreateUniqueSymbol('C'),
                "Synthetic Constraint Instrument",
                "USD",
                InstrumentType.Equity));
        await dbContext.SaveChangesAsync();

        SqlException invalidCurrencyException = await Assert.ThrowsAsync<SqlException>(
            () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [Instruments] ([Id], [Symbol], [Name], [Currency], [InstrumentType]) VALUES ({Guid.NewGuid()}, {CreateUniqueSymbol('X')}, {"Invalid Currency Probe"}, {"EUR"}, {1})"));
        Assert.Equal(547, invalidCurrencyException.Number);

        SqlException invalidPriceException = await Assert.ThrowsAsync<SqlException>(
            () => dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [MarketPrices] ([Id], [InstrumentId], [Price], [Currency], [AsOfUtc]) VALUES ({Guid.NewGuid()}, {instrumentId}, {0m}, {"USD"}, {DateTimeOffset.UtcNow})"));
        Assert.Equal(547, invalidPriceException.Number);
    }

    private ServiceProvider CreateServiceProvider()
    {
        ServiceCollection services = new();
        services.AddFinCoreInfrastructure(_fixture.TestConnectionString);
        return services.BuildServiceProvider();
    }

    private static async Task<int> ExecuteScalarIntAsync(FinCoreDbContext dbContext, string commandText)
    {
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = commandText;
            object? result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<Dictionary<string, string>> ReadNameAndActionAsync(
        FinCoreDbContext dbContext,
        string commandText)
    {
        Dictionary<string, string> results = new(StringComparer.Ordinal);
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = commandText;
            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(reader.GetString(0), reader.GetString(1));
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        return results;
    }

    private static async Task<List<IndexColumn>> ReadIndexColumnsAsync(FinCoreDbContext dbContext)
    {
        const string commandText = """
            SELECT i.name, i.is_unique, ic.key_ordinal, c.name, ic.is_descending_key
            FROM sys.indexes AS i
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            INNER JOIN sys.index_columns AS ic
                ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            INNER JOIN sys.columns AS c
                ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.name IN (
                N'PK_Portfolios',
                N'PK_Instruments',
                N'PK_Trades',
                N'PK_MarketPrices',
                N'UX_Instruments_Symbol',
                N'IX_Trades_InstrumentId',
                N'IX_Trades_Portfolio_Instrument_Execution',
                N'IX_MarketPrices_Instrument_Latest')
                AND ic.key_ordinal > 0
            ORDER BY i.name, ic.key_ordinal;
            """;

        List<IndexColumn> results = [];
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = commandText;
            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(
                    new IndexColumn(
                        reader.GetString(0),
                        reader.GetBoolean(1),
                        reader.GetByte(2),
                        reader.GetString(3),
                        reader.GetBoolean(4)));
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        return results;
    }

    private static void AssertIndexColumns(
        IReadOnlyCollection<IndexColumn> actualColumns,
        string indexName,
        bool isUnique,
        params (string ColumnName, bool IsDescending)[] expectedColumns)
    {
        IndexColumn[] indexColumns = actualColumns
            .Where(column => string.Equals(column.IndexName, indexName, StringComparison.Ordinal))
            .OrderBy(column => column.KeyOrdinal)
            .ToArray();

        Assert.Equal(expectedColumns.Length, indexColumns.Length);
        Assert.All(indexColumns, column => Assert.Equal(isUnique, column.IsUnique));

        for (int i = 0; i < expectedColumns.Length; i++)
        {
            Assert.Equal(expectedColumns[i].ColumnName, indexColumns[i].ColumnName);
            Assert.Equal(expectedColumns[i].IsDescending, indexColumns[i].IsDescending);
        }
    }

    private static string CreateUniqueSymbol(char prefix)
    {
        return $"{char.ToUpperInvariant(prefix)}{Guid.NewGuid():N}".ToUpperInvariant()[..20];
    }

    private sealed record IndexColumn(
        string IndexName,
        bool IsUnique,
        int KeyOrdinal,
        string ColumnName,
        bool IsDescending);
}
