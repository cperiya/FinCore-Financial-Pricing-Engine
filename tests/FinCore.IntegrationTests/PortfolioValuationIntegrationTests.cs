using System.Data.Common;
using FinCore.Application.Persistence;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Domain.Valuation;
using FinCore.Infrastructure.DependencyInjection;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.IntegrationTests;

/// <summary>
/// Verifies portfolio valuation queries and transaction consistency against the isolated SQL Server fixture.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class PortfolioValuationIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
    private readonly SqlServerTestDatabaseFixture _fixture;

    /// <summary>
    /// Initializes valuation tests with the fixture-owned migrated SQL Server database.
    /// </summary>
    /// <param name="fixture">The run-owned integration-test database fixture.</param>
    public PortfolioValuationIntegrationTests(SqlServerTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies historical trade and price cutoffs and deterministic batched latest-price selection.
    /// </summary>
    [SqlServerFact]
    public async Task PortfolioValuation_HistoricalCutoffsAndPriceTieBreakersAreApplied()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);

        await RecordTradeAsync(
            provider,
            portfolioId,
            instrumentId,
            TradeSide.Buy,
            100m,
            Start);
        await RecordTradeAsync(
            provider,
            portfolioId,
            instrumentId,
            TradeSide.Sell,
            20m,
            Start.AddHours(2));
        await RecordPriceAsync(provider, instrumentId, 450m, Start);
        await RecordPriceAsync(provider, instrumentId, 500m, Start.AddHours(2));
        await RecordPriceAsync(
            provider,
            instrumentId,
            525m,
            Start.AddHours(3),
            Guid.Parse("30000000-0000-0000-0000-000000000001"));
        await RecordPriceAsync(
            provider,
            instrumentId,
            550m,
            Start.AddHours(3),
            Guid.Parse("30000000-0000-0000-0000-000000000002"));

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IPortfolioValuationApplicationService valuationService =
            scope.ServiceProvider.GetRequiredService<IPortfolioValuationApplicationService>();

        PortfolioValuation historical = await valuationService.GetPortfolioValuationAsync(
            portfolioId,
            Start.AddHours(1),
            CancellationToken.None);
        PortfolioValuation current = await valuationService.GetPortfolioValuationAsync(
            portfolioId,
            Start.AddHours(3),
            CancellationToken.None);

        PortfolioValuationPosition historicalPosition = Assert.Single(historical.Positions);
        Assert.Equal(100m, historicalPosition.Quantity);
        Assert.Equal(450m, historicalPosition.MarketPrice);
        Assert.Equal(45_000m, historical.TotalMarketValue);

        PortfolioValuationPosition currentPosition = Assert.Single(current.Positions);
        Assert.Equal(80m, currentPosition.Quantity);
        Assert.Equal(550m, currentPosition.MarketPrice);
        Assert.Equal(Start.AddHours(3), currentPosition.PriceAsOfUtc);
        Assert.Equal(44_000m, current.TotalMarketValue);
    }

    /// <summary>
    /// Verifies a valuation concurrent with an atomic trade-and-price update observes a serializable state.
    /// </summary>
    [SqlServerFact]
    public async Task PortfolioValuation_ConcurrentTradeAndPriceCommit_IsConsistent()
    {
        await using ServiceProvider provider = CreateServiceProvider();
        (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
        await RecordTradeAsync(provider, portfolioId, instrumentId, TradeSide.Buy, 10m, Start);
        await RecordPriceAsync(provider, instrumentId, 10m, Start);

        TradeReadGateInterceptor readGate = new();
        WriteCommandGateInterceptor writeGate = new();
        await using ServiceProvider readProvider = CreateServiceProvider(readGate);
        await using ServiceProvider writerProvider = CreateServiceProvider(writeGate);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        await using (AsyncServiceScope readScope = readProvider.CreateAsyncScope())
        {
            Task<PortfolioValuation> valuationTask = readScope.ServiceProvider
                .GetRequiredService<IPortfolioValuationApplicationService>()
                .GetPortfolioValuationAsync(portfolioId, Start.AddMinutes(2), timeout.Token);
            Task? writeTask = null;

            try
            {
                await readGate.TradeReadReached.Task.WaitAsync(TimeSpan.FromSeconds(15), timeout.Token);
                writeTask = RecordTradeAndPriceAtomicallyAsync(
                    writerProvider,
                    portfolioId,
                    instrumentId,
                    timeout.Token);
                await writeGate.WriteCommandAttempted.Task.WaitAsync(TimeSpan.FromSeconds(15), timeout.Token);

                Assert.False(
                    valuationTask.IsCompleted,
                    "The valuation should remain active at the trade-read barrier while the write command is attempted.");
                Assert.False(
                    writeTask.IsCompleted,
                    "The write should remain at its command barrier until the test releases it.");
            }
            finally
            {
                readGate.Release();
                writeGate.Release();
            }

            PortfolioValuation valuation = await valuationTask.WaitAsync(timeout.Token);
            Task completedWriteTask = writeTask ??
                throw new InvalidOperationException("The concurrent write was not started.");
            await completedWriteTask.WaitAsync(timeout.Token);

            PortfolioValuationPosition position = Assert.Single(valuation.Positions);
            Assert.True(
                (position.Quantity == 10m && position.MarketPrice == 10m) ||
                (position.Quantity == 5m && position.MarketPrice == 20m),
                $"Observed a mixed valuation state: quantity {position.Quantity}, price {position.MarketPrice}.");
            Assert.Equal(position.Quantity * position.MarketPrice, valuation.TotalMarketValue);
        }
    }

    private ServiceProvider CreateServiceProvider(params IInterceptor[] interceptors)
    {
        ServiceCollection services = new();
        services.AddFinCoreInfrastructure(_fixture.TestConnectionString);
        if (interceptors.Length > 0)
        {
            services.AddDbContext<FinCoreDbContext>(options => options.AddInterceptors(interceptors));
        }

        services.AddSingleton(new PositionCalculationService());
        services.AddSingleton(new StockValuationService());
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<IPortfolioValuationApplicationService, PortfolioValuationApplicationService>();
        services.AddScoped<ITradeApplicationService, TradeApplicationService>();
        return services.BuildServiceProvider();
    }

    private static async Task RecordTradeAndPriceAtomicallyAsync(
        ServiceProvider provider,
        Guid portfolioId,
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        ITradeRepository tradeRepository = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        IMarketPriceRepository marketPriceRepository =
            scope.ServiceProvider.GetRequiredService<IMarketPriceRepository>();

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await tradeRepository.AddAsync(
                    new Trade(
                        Guid.NewGuid(),
                        portfolioId,
                        instrumentId,
                        TradeSide.Sell,
                        5m,
                        11m,
                        Start.AddMinutes(1)),
                    token);
                await unitOfWork.SaveChangesAsync(token);
                await marketPriceRepository.AddAsync(
                    new MarketPrice(
                        Guid.NewGuid(),
                        instrumentId,
                        20m,
                        "USD",
                        Start.AddMinutes(1)),
                    token);
                await unitOfWork.SaveChangesAsync(token);
                return true;
            },
            cancellationToken);
    }

    private sealed class TradeReadGateInterceptor : DbCommandInterceptor
    {
        private int _hasPaused;

        internal TaskCompletionSource<bool> TradeReadReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<bool> ContinueValuation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[Trades]", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _hasPaused, 1) == 0)
            {
                TradeReadReached.TrySetResult(true);
                await ContinueValuation.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return result;
        }

        internal void Release()
        {
            ContinueValuation.TrySetResult(true);
        }
    }

    private sealed class WriteCommandGateInterceptor : DbCommandInterceptor
    {
        private int _hasPaused;

        internal TaskCompletionSource<bool> WriteCommandAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<bool> ContinueWrite { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await PauseFirstInsertAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await PauseFirstInsertAsync(command, cancellationToken);
            return result;
        }

        internal void Release()
        {
            ContinueWrite.TrySetResult(true);
        }

        private async ValueTask PauseFirstInsertAsync(
            DbCommand command,
            CancellationToken cancellationToken)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _hasPaused, 1) == 0)
            {
                WriteCommandAttempted.TrySetResult(true);
                await ContinueWrite.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
        }
    }

    private static async Task<(Guid PortfolioId, Guid InstrumentId)> CreatePortfolioAndInstrumentAsync(
        ServiceProvider provider)
    {
        Guid portfolioId = Guid.NewGuid();
        Guid instrumentId = Guid.NewGuid();
        string symbol = $"V{Guid.NewGuid():N}"[..12];
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPortfolioRepository>().AddAsync(
            new Portfolio(portfolioId, "Synthetic Valuation Portfolio", "USD", Start),
            CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().AddAsync(
            new Instrument(instrumentId, symbol, "Synthetic Valuation Equity", "USD", InstrumentType.Equity),
            CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return (portfolioId, instrumentId);
    }

    private static async Task RecordTradeAsync(
        ServiceProvider provider,
        Guid portfolioId,
        Guid instrumentId,
        TradeSide side,
        decimal quantity,
        DateTimeOffset executedAt)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ITradeApplicationService>().RecordTradeAsync(
            Guid.NewGuid(),
            portfolioId,
            instrumentId,
            side,
            quantity,
            25m,
            executedAt,
            CancellationToken.None);
    }

    private static async Task RecordPriceAsync(
        ServiceProvider provider,
        Guid instrumentId,
        decimal price,
        DateTimeOffset asOfUtc,
        Guid? priceId = null)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMarketPriceRepository>().AddAsync(
            new MarketPrice(priceId ?? Guid.NewGuid(), instrumentId, price, "USD", asOfUtc),
            CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }
}
