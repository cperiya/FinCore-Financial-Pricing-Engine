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
        await using ServiceProvider jsonParameterProvider =
            CreateServiceProvider(useJsonCollectionParameter: true);
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

        await AssertHistoricalValuationAsync(provider, portfolioId);
        await AssertHistoricalValuationAsync(jsonParameterProvider, portfolioId);
    }

    /// <summary>
    /// Verifies JSON-parameter collection translation preserves explicit missing-price failures.
    /// </summary>
    [SqlServerFact]
    public async Task PortfolioValuation_JsonCollectionParameter_PreservesMissingPriceFailure()
    {
        foreach (bool useJsonCollectionParameter in new[] { false, true })
        {
            await using ServiceProvider provider =
                CreateServiceProvider(useJsonCollectionParameter: useJsonCollectionParameter);
            (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
            await RecordTradeAsync(provider, portfolioId, instrumentId, TradeSide.Buy, 10m, Start);

            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            IPortfolioValuationApplicationService valuationService =
                scope.ServiceProvider.GetRequiredService<IPortfolioValuationApplicationService>();

            MissingMarketPricesException exception =
                await Assert.ThrowsAsync<MissingMarketPricesException>(
                    () => valuationService.GetPortfolioValuationAsync(
                        portfolioId,
                        Start.AddHours(1),
                        CancellationToken.None));

            Assert.Equal(portfolioId, exception.PortfolioId);
            Assert.Equal(instrumentId, Assert.Single(exception.MissingInstruments).InstrumentId);
        }
    }

    /// <summary>
    /// Verifies exact decimal valuation, sparse history, inclusive cutoffs, future exclusion, and ID tie-breaking
    /// in both collection-parameter modes.
    /// </summary>
    [SqlServerFact]
    public async Task PortfolioValuation_ExactAmountsAndPriceSelectionMatchBothTranslations()
    {
        foreach (bool useJsonCollectionParameter in new[] { false, true })
        {
            await using ServiceProvider provider =
                CreateServiceProvider(useJsonCollectionParameter: useJsonCollectionParameter);
            (Guid portfolioId, Guid firstInstrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
            Guid secondInstrumentId = await AddInstrumentAsync(provider);

            await RecordTradeAsync(
                provider,
                portfolioId,
                firstInstrumentId,
                TradeSide.Buy,
                10.0000000001m,
                Start,
                25.1234567890m);
            await RecordTradeAsync(
                provider,
                portfolioId,
                firstInstrumentId,
                TradeSide.Buy,
                2.0000000002m,
                Start.AddMinutes(1),
                25.1234567890m);
            await RecordTradeAsync(
                provider,
                portfolioId,
                firstInstrumentId,
                TradeSide.Sell,
                1.0000000001m,
                Start.AddMinutes(2),
                25.1234567890m);
            await RecordPriceAsync(provider, firstInstrumentId, 9m, Start.AddDays(-1));
            string tiePrefix = Guid.NewGuid().ToString("N")[..20];
            await RecordPriceAsync(
                provider,
                firstInstrumentId,
                12.1234567890m,
                Start.AddHours(1),
                Guid.ParseExact($"{tiePrefix}000000000001", "N"));
            await RecordPriceAsync(
                provider,
                firstInstrumentId,
                11.1234567890m,
                Start.AddHours(1),
                Guid.ParseExact($"{tiePrefix}000000000002", "N"));
            await RecordPriceAsync(provider, firstInstrumentId, 999m, Start.AddHours(3));

            await RecordTradeAsync(
                provider,
                portfolioId,
                secondInstrumentId,
                TradeSide.Buy,
                0.1250000001m,
                Start.AddMinutes(5),
                25.1234567890m);
            await RecordPriceAsync(provider, secondInstrumentId, 3.3333333333m, Start.AddHours(2));
            await RecordPriceAsync(provider, secondInstrumentId, 100m, Start.AddHours(3));

            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            PortfolioValuation valuation = await scope.ServiceProvider
                .GetRequiredService<IPortfolioValuationApplicationService>()
                .GetPortfolioValuationAsync(
                    portfolioId,
                    Start.AddHours(2),
                    CancellationToken.None);

            PortfolioValuationPosition firstPosition = Assert.Single(
                valuation.Positions,
                position => position.InstrumentId == firstInstrumentId);
            PortfolioValuationPosition secondPosition = Assert.Single(
                valuation.Positions,
                position => position.InstrumentId == secondInstrumentId);
            Assert.Equal(11.0000000002m, firstPosition.Quantity);
            Assert.Equal(11.1234567890m, firstPosition.MarketPrice);
            Assert.Equal(Start.AddHours(1), firstPosition.PriceAsOfUtc);
            Assert.Equal(0.1250000001m, secondPosition.Quantity);
            Assert.Equal(3.3333333333m, secondPosition.MarketPrice);
            Assert.Equal(Start.AddHours(2), secondPosition.PriceAsOfUtc);
            Assert.Equal(
                checked(
                    checked(firstPosition.Quantity * firstPosition.MarketPrice) +
                    checked(secondPosition.Quantity * secondPosition.MarketPrice)),
                valuation.TotalMarketValue);
        }
    }

    /// <summary>
    /// Verifies valuation overflow and cancellation are surfaced consistently in scalar and JSON modes.
    /// </summary>
    [SqlServerFact]
    public async Task PortfolioValuation_OverflowAndCancellationMatchBothTranslations()
    {
        const decimal maximumPersistedDecimal = 999999999999999999.9999999999m;

        foreach (bool useJsonCollectionParameter in new[] { false, true })
        {
            await using ServiceProvider provider =
                CreateServiceProvider(useJsonCollectionParameter: useJsonCollectionParameter);
            (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
            await RecordTradeAsync(
                provider,
                portfolioId,
                instrumentId,
                TradeSide.Buy,
                maximumPersistedDecimal,
                Start,
                1m);
            await RecordPriceAsync(
                provider,
                instrumentId,
                maximumPersistedDecimal,
                Start);

            await using AsyncServiceScope overflowScope = provider.CreateAsyncScope();
            IPortfolioValuationApplicationService overflowService =
                overflowScope.ServiceProvider.GetRequiredService<IPortfolioValuationApplicationService>();
            await Assert.ThrowsAsync<PortfolioValuationOverflowException>(
                () => overflowService.GetPortfolioValuationAsync(
                    portfolioId,
                    Start.AddMinutes(1),
                    CancellationToken.None));

            (Guid cancellablePortfolioId, Guid cancellableInstrumentId) =
                await CreatePortfolioAndInstrumentAsync(provider);
            await RecordTradeAsync(
                provider,
                cancellablePortfolioId,
                cancellableInstrumentId,
                TradeSide.Buy,
                1m,
                Start);
            await RecordPriceAsync(provider, cancellableInstrumentId, 10m, Start);

            InstrumentReadGateInterceptor readGate = new();
            await using ServiceProvider cancellationProvider =
                CreateServiceProvider(useJsonCollectionParameter, readGate);
            using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(20));
            await using AsyncServiceScope cancellationScope = cancellationProvider.CreateAsyncScope();
            Task<PortfolioValuation> valuationTask = cancellationScope.ServiceProvider
                .GetRequiredService<IPortfolioValuationApplicationService>()
                .GetPortfolioValuationAsync(
                    cancellablePortfolioId,
                    Start.AddMinutes(1),
                    cancellation.Token);

            try
            {
                await readGate.InstrumentReadReached.Task.WaitAsync(
                    TimeSpan.FromSeconds(15),
                    cancellation.Token);
                cancellation.Cancel();
            }
            finally
            {
                readGate.Release();
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await valuationTask.WaitAsync(TimeSpan.FromSeconds(15)));
        }
    }

    /// <summary>
    /// Verifies a valuation concurrent with an atomic trade-and-price update observes a serializable state.
    /// </summary>
    [SqlServerFact]
    public async Task PortfolioValuation_ConcurrentTradeAndPriceCommit_IsConsistent()
    {
        foreach (bool useJsonCollectionParameter in new[] { false, true })
        {
            await using ServiceProvider provider =
                CreateServiceProvider(useJsonCollectionParameter: useJsonCollectionParameter);
            (Guid portfolioId, Guid instrumentId) = await CreatePortfolioAndInstrumentAsync(provider);
            await RecordTradeAsync(provider, portfolioId, instrumentId, TradeSide.Buy, 10m, Start);
            await RecordPriceAsync(provider, instrumentId, 10m, Start);

            TradeReadGateInterceptor readGate = new();
            WriteCommandAttemptInterceptor writeAttempt = new();
            await using ServiceProvider readProvider =
                CreateServiceProvider(useJsonCollectionParameter, readGate);
            await using ServiceProvider writerProvider =
                CreateServiceProvider(useJsonCollectionParameter, writeAttempt);
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
                    await writeAttempt.WriteCommandAttempted.Task.WaitAsync(TimeSpan.FromSeconds(15), timeout.Token);
                    await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);

                    Assert.False(
                        valuationTask.IsCompleted,
                        "The valuation should remain active at the trade-read barrier while the write command is attempted.");
                    Assert.False(
                        writeTask.IsCompleted,
                        "The atomic write should remain blocked by the serializable valuation transaction.");
                }
                finally
                {
                    readGate.Release();
                }

                PortfolioValuation valuation = await valuationTask.WaitAsync(timeout.Token);
                Task completedWriteTask = writeTask ??
                    throw new InvalidOperationException("The concurrent write was not started.");
                await completedWriteTask.WaitAsync(timeout.Token);

                PortfolioValuationPosition position = Assert.Single(valuation.Positions);
                Assert.True(
                    position.Quantity == 10m && position.MarketPrice == 10m,
                    $"Observed a mixed valuation state: quantity {position.Quantity}, price {position.MarketPrice}.");
                Assert.Equal(position.Quantity * position.MarketPrice, valuation.TotalMarketValue);
            }

            await using AsyncServiceScope verificationScope = readProvider.CreateAsyncScope();
            PortfolioValuation updatedValuation = await verificationScope.ServiceProvider
                .GetRequiredService<IPortfolioValuationApplicationService>()
                .GetPortfolioValuationAsync(
                    portfolioId,
                    Start.AddMinutes(2),
                    timeout.Token);
            PortfolioValuationPosition updatedPosition = Assert.Single(updatedValuation.Positions);
            Assert.Equal(5m, updatedPosition.Quantity);
            Assert.Equal(20m, updatedPosition.MarketPrice);
            Assert.Equal(100m, updatedValuation.TotalMarketValue);
        }
    }

    private ServiceProvider CreateServiceProvider(params IInterceptor[] interceptors)
    {
        return CreateServiceProvider(useJsonCollectionParameter: false, interceptors);
    }

    private ServiceProvider CreateServiceProvider(
        bool useJsonCollectionParameter,
        params IInterceptor[] interceptors)
    {
        ServiceCollection services = new();
        services.AddFinCoreInfrastructure(_fixture.TestConnectionString);
        if (useJsonCollectionParameter)
        {
            services.Configure<PortfolioCollectionParameterOptions>(
                options => options.UseJsonParameterForIds = true);
        }

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

    private static async Task AssertHistoricalValuationAsync(
        ServiceProvider provider,
        Guid portfolioId)
    {
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

    private sealed class WriteCommandAttemptInterceptor : DbCommandInterceptor
    {
        private int _hasSignaled;

        internal TaskCompletionSource<bool> WriteCommandAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SignalFirstInsert(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            SignalFirstInsert(command);
            return ValueTask.FromResult(result);
        }

        private void SignalFirstInsert(DbCommand command)
        {
            if (command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _hasSignaled, 1) == 0)
            {
                WriteCommandAttempted.TrySetResult(true);
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

    private static async Task<Guid> AddInstrumentAsync(ServiceProvider provider)
    {
        Guid instrumentId = Guid.NewGuid();
        string symbol = $"V{Guid.NewGuid():N}"[..12];
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInstrumentRepository>().AddAsync(
            new Instrument(instrumentId, symbol, "Synthetic second valuation equity", "USD", InstrumentType.Equity),
            CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        return instrumentId;
    }

    private static async Task RecordTradeAsync(
        ServiceProvider provider,
        Guid portfolioId,
        Guid instrumentId,
        TradeSide side,
        decimal quantity,
        DateTimeOffset executedAt,
        decimal unitPrice = 25m)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ITradeApplicationService>().RecordTradeAsync(
            Guid.NewGuid(),
            portfolioId,
            instrumentId,
            side,
            quantity,
            unitPrice,
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

    private sealed class InstrumentReadGateInterceptor : DbCommandInterceptor
    {
        private int _hasPaused;

        internal TaskCompletionSource<bool> InstrumentReadReached { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<bool> ContinueValuation { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
        {
        if (command.CommandText.Contains("[Instruments]", StringComparison.OrdinalIgnoreCase) &&
            Interlocked.Exchange(ref _hasPaused, 1) == 0)
        {
            InstrumentReadReached.TrySetResult(true);
            await ContinueValuation.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }

        return result;
        }

        internal void Release()
        {
        ContinueValuation.TrySetResult(true);
        }
    }
}
