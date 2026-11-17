using System.Data;
using FinCore.Application.Persistence;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.DependencyInjection;
using FinCore.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.IntegrationTests;

/// <summary>
/// Verifies transaction-safe trade use cases against the fixture's isolated SQL Server database.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class TradeApplicationIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly SqlServerTestDatabaseFixture _fixture;

    /// <summary>
    /// Initializes trade integration tests with the isolated database fixture.
    /// </summary>
    /// <param name="fixture">The SQL Server database owned by the current test run.</param>
    public TradeApplicationIntegrationTests(SqlServerTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies only one of two concurrent seven-share sells can commit against ten available shares.
    /// </summary>
    [SqlServerFact]
    public async Task TradeService_ConcurrentSellsSerializeAndPreventOverselling()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
        await RecordTradeAsync(
            provider,
            Guid.NewGuid(),
            portfolioId,
            instrumentId,
            TradeSide.Buy,
            10m,
            20m,
            Start);

        Task<bool> firstSell = TryRecordSellAsync(
            provider, Guid.NewGuid(), portfolioId, instrumentId, Start.AddMinutes(1));
        Task<bool> secondSell = TryRecordSellAsync(
            provider, Guid.NewGuid(), portfolioId, instrumentId, Start.AddMinutes(1));
        bool[] outcomes = await Task.WhenAll(firstSell, secondSell);

        Assert.Single(outcomes, succeeded => succeeded);
        Assert.Single(outcomes, succeeded => !succeeded);

        await using AsyncServiceScope verificationScope = provider.CreateAsyncScope();
        IReadOnlyList<Trade> trades = await verificationScope.ServiceProvider
            .GetRequiredService<ITradeRepository>()
            .GetByPortfolioAndInstrumentAsync(portfolioId, instrumentId, CancellationToken.None);
        Position position = Assert.Single(new PositionCalculationService().CalculatePositions(trades));

        Assert.Equal(2, trades.Count);
        Assert.Equal(3m, position.Quantity);
    }

    /// <summary>
    /// Verifies a valid backdated buy is accepted and a backdated sell that breaks history is rolled back.
    /// </summary>
    [SqlServerFact]
    public async Task TradeService_BackdatedTradesValidateCompleteChronologicalHistory()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        (Guid validPortfolioId, Guid validInstrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
        await RecordTradeAsync(
            provider,
            Guid.NewGuid(),
            validPortfolioId,
            validInstrumentId,
            TradeSide.Buy,
            10m,
            20m,
            Start.AddHours(2));

        await RecordTradeAsync(
            provider,
            Guid.NewGuid(),
            validPortfolioId,
            validInstrumentId,
            TradeSide.Buy,
            5m,
            19m,
            Start);

        (Guid invalidPortfolioId, Guid invalidInstrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
        await RecordTradeAsync(
            provider,
            Guid.NewGuid(),
            invalidPortfolioId,
            invalidInstrumentId,
            TradeSide.Buy,
            10m,
            20m,
            Start.AddHours(2));

        Guid rejectedTradeId = Guid.NewGuid();
        await Assert.ThrowsAsync<InsufficientPositionException>(
            () => RecordTradeAsync(
                provider,
                rejectedTradeId,
                invalidPortfolioId,
                invalidInstrumentId,
                TradeSide.Sell,
                1m,
                21m,
                Start));

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IReadOnlyList<Trade> validHistory = await scope.ServiceProvider
            .GetRequiredService<ITradeRepository>()
            .GetByPortfolioAndInstrumentAsync(validPortfolioId, validInstrumentId, CancellationToken.None);
        IReadOnlyList<Trade> invalidHistory = await scope.ServiceProvider
            .GetRequiredService<ITradeRepository>()
            .GetByPortfolioAndInstrumentAsync(invalidPortfolioId, invalidInstrumentId, CancellationToken.None);

        Assert.Equal(2, validHistory.Count);
        Assert.Single(invalidHistory);
        Assert.DoesNotContain(invalidHistory, trade => trade.Id == rejectedTradeId);
    }

    /// <summary>
    /// Verifies equal execution timestamps use Domain buy-before-sell ordering.
    /// </summary>
    [SqlServerFact]
    public async Task TradeService_EqualExecutionTimestampsUseDeterministicDomainOrdering()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);

        await Assert.ThrowsAsync<InsufficientPositionException>(
            () => RecordTradeAsync(
                provider, Guid.NewGuid(), portfolioId, instrumentId, TradeSide.Sell, 2m, 21m, Start));

        await RecordTradeAsync(
            provider, Guid.NewGuid(), portfolioId, instrumentId, TradeSide.Buy, 3m, 20m, Start);
        await RecordTradeAsync(
            provider, Guid.NewGuid(), portfolioId, instrumentId, TradeSide.Sell, 2m, 21m, Start);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IReadOnlyList<Trade> trades = await scope.ServiceProvider
            .GetRequiredService<ITradeRepository>()
            .GetByPortfolioAndInstrumentAsync(portfolioId, instrumentId, CancellationToken.None);
        Position position = Assert.Single(new PositionCalculationService().CalculatePositions(trades));

        Assert.Equal(1m, position.Quantity);
    }

    /// <summary>
    /// Verifies identical trade retries return the stored trade and conflicting payloads are rejected.
    /// </summary>
    [SqlServerFact]
    public async Task TradeService_IdempotentReplayReturnsStoredTradeAndConflictsAreRejected()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
        Guid tradeId = Guid.NewGuid();

        Trade first = await RecordTradeAsync(
            provider, tradeId, portfolioId, instrumentId, TradeSide.Buy, 5m, 20m, Start);
        Trade replay = await RecordTradeAsync(
            provider, tradeId, portfolioId, instrumentId, TradeSide.Buy, 5.00m, 20.00m, Start);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.Quantity, replay.Quantity);
        await Assert.ThrowsAsync<TradeIdempotencyConflictException>(
            () => RecordTradeAsync(
                provider, tradeId, portfolioId, instrumentId, TradeSide.Buy, 6m, 20m, Start));

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IReadOnlyList<Trade> trades = await scope.ServiceProvider
            .GetRequiredService<ITradeRepository>()
            .GetByPortfolioAndInstrumentAsync(portfolioId, instrumentId, CancellationToken.None);
        Assert.Single(trades);
    }

    /// <summary>
    /// Verifies a failed financial-scale save rolls back and the same trade ID can then be retried safely.
    /// </summary>
    [SqlServerFact]
    public async Task TradeService_SaveFailureRollsBackAndAllowsRetryWithSameId()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
        Guid tradeId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RecordTradeAsync(
                provider,
                tradeId,
                portfolioId,
                instrumentId,
                TradeSide.Buy,
                1.00000000001m,
                20m,
                Start));

        Trade committed = await RecordTradeAsync(
            provider,
            tradeId,
            portfolioId,
            instrumentId,
            TradeSide.Buy,
            1m,
            20m,
            Start);
        Trade replay = await RecordTradeAsync(
            provider,
            tradeId,
            portfolioId,
            instrumentId,
            TradeSide.Buy,
            1m,
            20m,
            Start);

        Assert.Equal(committed.Id, replay.Id);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IReadOnlyList<Trade> trades = await scope.ServiceProvider
            .GetRequiredService<ITradeRepository>()
            .GetByPortfolioAndInstrumentAsync(portfolioId, instrumentId, CancellationToken.None);
        Assert.Single(trades);
    }

    /// <summary>
    /// Verifies cancellation while waiting for a position lock rolls back without inserting a trade.
    /// </summary>
    [SqlServerFact]
    public async Task TradeService_CancellationWhileWaitingForLockDoesNotInsertTrade()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
        string positionResource = GetPositionLockResource(portfolioId, instrumentId);

        await using SqlConnection lockConnection = new(_fixture.TestConnectionString);
        await lockConnection.OpenAsync();
        await using SqlTransaction lockTransaction =
            (SqlTransaction)await lockConnection.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireExternalLockAsync(lockConnection, lockTransaction, positionResource);

        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RecordTradeAsync(
                provider,
                Guid.NewGuid(),
                portfolioId,
                instrumentId,
                TradeSide.Buy,
                1m,
                20m,
                Start,
                cancellation.Token));

        await lockTransaction.RollbackAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IReadOnlyList<Trade> trades = await scope.ServiceProvider
            .GetRequiredService<ITradeRepository>()
            .GetByPortfolioAndInstrumentAsync(portfolioId, instrumentId, CancellationToken.None);
        Assert.Empty(trades);
    }

    /// <summary>
    /// Verifies a SQL Server application-lock timeout is surfaced explicitly.
    /// </summary>
    [SqlServerFact]
    public async Task UnitOfWork_WhenApplicationLockTimesOut_ThrowsTimeoutException()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
        string positionResource = GetPositionLockResource(portfolioId, instrumentId);

        await using SqlConnection lockConnection = new(_fixture.TestConnectionString);
        await lockConnection.OpenAsync();
        await using SqlTransaction lockTransaction =
            (SqlTransaction)await lockConnection.BeginTransactionAsync(IsolationLevel.Serializable);
        await AcquireExternalLockAsync(lockConnection, lockTransaction, positionResource);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await Assert.ThrowsAsync<TimeoutException>(
            () => unitOfWork.ExecuteWithExclusiveLocksAsync(
                [positionResource],
                _ => Task.FromResult(true),
                CancellationToken.None));

        await lockTransaction.RollbackAsync();
    }

    private ServiceProvider CreateServiceProvider()
    {
        ServiceCollection services = new();
        services.AddFinCoreInfrastructure(_fixture.TestConnectionString);
        services.AddSingleton<PositionCalculationService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<ITradeApplicationService, TradeApplicationService>();
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid PortfolioId, Guid InstrumentId)> CreatePortfolioAndInstrumentAsync(
        IServiceProvider provider)
    {
        Guid portfolioId = Guid.NewGuid();
        Guid instrumentId = Guid.NewGuid();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPortfolioRepository>()
            .AddAsync(new Portfolio(portfolioId, "Synthetic Trade Test", "USD", Start), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>()
            .AddAsync(
                new Instrument(
                    instrumentId,
                    CreateUniqueSymbol(),
                    "Synthetic Trade Instrument",
                    "USD",
                    InstrumentType.Equity),
                CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync(CancellationToken.None);
        return (portfolioId, instrumentId);
    }

    private static async Task<Trade> RecordTradeAsync(
        IServiceProvider provider,
        Guid tradeId,
        Guid portfolioId,
        Guid instrumentId,
        TradeSide side,
        decimal quantity,
        decimal unitPrice,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITradeApplicationService>()
            .RecordTradeAsync(
                tradeId,
                portfolioId,
                instrumentId,
                side,
                quantity,
                unitPrice,
                executedAt,
                cancellationToken);
    }

    private static async Task<bool> TryRecordSellAsync(
        IServiceProvider provider,
        Guid tradeId,
        Guid portfolioId,
        Guid instrumentId,
        DateTimeOffset executedAt)
    {
        try
        {
            await RecordTradeAsync(
                provider,
                tradeId,
                portfolioId,
                instrumentId,
                TradeSide.Sell,
                7m,
                21m,
                executedAt);
            return true;
        }
        catch (InsufficientPositionException)
        {
            return false;
        }
    }

    private static async Task AcquireExternalLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string resource)
    {
        await using SqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = @ResourceName,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 0,
                @DbPrincipal = N'public';
            SELECT @LockResult;
            """;
        command.Parameters.Add("@ResourceName", System.Data.SqlDbType.NVarChar, 255).Value = resource;
        int result = Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(result >= 0, $"The test could not acquire its SQL application lock (result {result}).");
    }

    private static string GetPositionLockResource(Guid portfolioId, Guid instrumentId)
    {
        return $"FinCore:Position:{portfolioId:N}:{instrumentId:N}";
    }

    private static string CreateUniqueSymbol()
    {
        return $"T{Guid.NewGuid():N}"[..20].ToUpperInvariant();
    }
}
