using FinCore.Application.Persistence;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Domain.Valuation;

namespace FinCore.UnitTests;

/// <summary>
/// Verifies portfolio Application use cases using in-memory repository contracts.
/// </summary>
public sealed class PortfolioApplicationServiceTests
{
    private static readonly Guid PortfolioId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid InstrumentId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies portfolio creation uses USD and the injected UTC clock.
    /// </summary>
    [Fact]
    public async Task PortfolioService_CreatesUsdPortfolioAtCurrentUtcTime()
    {
        InMemoryStore store = new();
        PortfolioApplicationService service = CreatePortfolioService(store, Start);

        Portfolio portfolio = await service.CreatePortfolioAsync("  Growth  ", CancellationToken.None);

        Assert.Equal("Growth", portfolio.Name);
        Assert.Equal("USD", portfolio.BaseCurrency);
        Assert.Equal(Start, portfolio.CreatedAt);
        Assert.Same(portfolio, Assert.Single(store.Portfolios));
    }

    /// <summary>
    /// Verifies instrument symbols are normalized and duplicate normalized symbols are rejected.
    /// </summary>
    [Fact]
    public async Task InstrumentService_NormalizesSymbolsAndRejectsDuplicate()
    {
        InMemoryStore store = new();
        InstrumentApplicationService service = new(store, store);

        Instrument instrument = await service.RegisterEquityAsync(" msft ", "Microsoft", CancellationToken.None);

        Assert.Equal("MSFT", instrument.Symbol);
        Assert.Equal("USD", instrument.Currency);
        Assert.Equal(InstrumentType.Equity, instrument.InstrumentType);
        await Assert.ThrowsAsync<InstrumentSymbolAlreadyRegisteredException>(
            () => service.RegisterEquityAsync("MSFT", "Another name", CancellationToken.None));
        Assert.Single(store.Instruments);
    }

    /// <summary>
    /// Verifies a BUY followed by a SELL produces the expected derived position.
    /// </summary>
    [Fact]
    public async Task TradeService_BuyThenSell_ReturnsRemainingPosition()
    {
        InMemoryStore store = CreateStoreWithParents();
        TradeApplicationService service = CreateTradeService(store);

        await service.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 10m, 20m, Start, CancellationToken.None);
        await service.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Sell, 4m, 22m, Start.AddMinutes(1),
            CancellationToken.None);
        PortfolioApplicationService portfolios = CreatePortfolioService(store, Start);

        Position position = Assert.Single(await portfolios.GetPositionsAsync(PortfolioId, CancellationToken.None));

        Assert.Equal(6m, position.Quantity);
    }

    /// <summary>
    /// Verifies backdated trades are accepted only when the complete chronological history remains long-only.
    /// </summary>
    [Fact]
    public async Task TradeService_BackdatedTradesValidateCompleteChronology()
    {
        InMemoryStore validStore = CreateStoreWithParents();
        TradeApplicationService validService = CreateTradeService(validStore);
        await validService.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 10m, 10m, Start.AddHours(2),
            CancellationToken.None);

        await validService.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 5m, 9m, Start,
            CancellationToken.None);

        Assert.Equal(2, validStore.Trades.Count);

        InMemoryStore invalidStore = CreateStoreWithParents();
        TradeApplicationService invalidService = CreateTradeService(invalidStore);
        await invalidService.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 10m, 10m, Start.AddHours(2),
            CancellationToken.None);

        await Assert.ThrowsAsync<InsufficientPositionException>(
            () => invalidService.RecordTradeAsync(
                Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Sell, 1m, 11m, Start,
                CancellationToken.None));

        Assert.Single(invalidStore.Trades);
    }

    /// <summary>
    /// Verifies identical execution timestamps follow buy-before-sell ordering.
    /// </summary>
    [Fact]
    public async Task TradeService_EqualExecutionTimestampsUseDomainOrdering()
    {
        InMemoryStore store = CreateStoreWithParents();
        TradeApplicationService service = CreateTradeService(store);

        await Assert.ThrowsAsync<InsufficientPositionException>(
            () => service.RecordTradeAsync(
                Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Sell, 2m, 11m, Start,
                CancellationToken.None));

        await service.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 3m, 10m, Start, CancellationToken.None);
        await service.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Sell, 2m, 11m, Start, CancellationToken.None);

        PortfolioApplicationService portfolios = CreatePortfolioService(store, Start);
        Position position = Assert.Single(await portfolios.GetPositionsAsync(PortfolioId, CancellationToken.None));
        Assert.Equal(1m, position.Quantity);
    }

    /// <summary>
    /// Verifies identical trade requests replay the stored entity and conflicting payloads are rejected.
    /// </summary>
    [Fact]
    public async Task TradeService_IdempotencyReturnsExactReplayAndRejectsConflict()
    {
        InMemoryStore store = CreateStoreWithParents();
        TradeApplicationService service = CreateTradeService(store);
        Guid tradeId = Guid.NewGuid();

        Trade first = await service.RecordTradeAsync(
            tradeId, PortfolioId, InstrumentId, TradeSide.Buy, 5m, 10m, Start, CancellationToken.None);
        Trade replay = await service.RecordTradeAsync(
            tradeId, PortfolioId, InstrumentId, TradeSide.Buy, 5.00m, 10.00m, Start, CancellationToken.None);

        Assert.Same(first, replay);
        Assert.Single(store.Trades);
        await Assert.ThrowsAsync<TradeIdempotencyConflictException>(
            () => service.RecordTradeAsync(
                tradeId, PortfolioId, InstrumentId, TradeSide.Buy, 6m, 10m, Start, CancellationToken.None));
        Assert.Single(store.Trades);
    }

    /// <summary>
    /// Verifies latest-price lookup excludes observations after the requested UTC cutoff.
    /// </summary>
    [Fact]
    public async Task MarketPriceService_ReturnsLatestEligiblePriceOrNull()
    {
        InMemoryStore store = CreateStoreWithParents();
        MarketPriceApplicationService service = new(store, store, store);
        await service.RecordPriceAsync(InstrumentId, 10m, Start, CancellationToken.None);
        await service.RecordPriceAsync(InstrumentId, 12m, Start.AddHours(2), CancellationToken.None);

        MarketPrice? eligible = await service.GetLatestEligiblePriceAsync(
            InstrumentId, Start.AddHours(1), CancellationToken.None);
        MarketPrice? absent = await service.GetLatestEligiblePriceAsync(
            InstrumentId, Start.AddMinutes(-1), CancellationToken.None);

        Assert.Equal(10m, eligible?.Price);
        Assert.Null(absent);
    }

    /// <summary>
    /// Verifies an omitted cutoff is captured once and an empty portfolio values to zero.
    /// </summary>
    [Fact]
    public async Task ValuationService_EmptyPortfolioUsesSingleCurrentUtcCutoff()
    {
        InMemoryStore store = CreateStoreWithParents();
        CountingTimeProvider clock = new(Start);
        PortfolioValuationApplicationService service = CreateValuationService(store, clock);

        PortfolioValuation valuation = await service.GetPortfolioValuationAsync(
            PortfolioId,
            null,
            CancellationToken.None);

        Assert.Equal(Start, valuation.AsOfUtc);
        Assert.Equal("USD", valuation.Currency);
        Assert.Empty(valuation.Positions);
        Assert.Equal(0m, valuation.TotalMarketValue);
        Assert.Equal(1, clock.CallCount);
    }

    /// <summary>
    /// Verifies buys and partial sales value the remaining quantity without rounding.
    /// </summary>
    [Fact]
    public async Task ValuationService_Buy100Sell20_ValuesRemaining80Exactly()
    {
        InMemoryStore store = CreateStoreWithParents();
        TradeApplicationService trades = CreateTradeService(store);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 100m, 10m, Start, CancellationToken.None);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Sell, 20m, 11m, Start.AddMinutes(1),
            CancellationToken.None);
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.NewGuid(), InstrumentId, 450m, "USD", Start),
            CancellationToken.None);

        PortfolioValuation valuation = await CreateValuationService(store).GetPortfolioValuationAsync(
            PortfolioId,
            Start.AddMinutes(2),
            CancellationToken.None);

        PortfolioValuationPosition position = Assert.Single(valuation.Positions);
        Assert.Equal(80m, position.Quantity);
        Assert.Equal(450m, position.MarketPrice);
        Assert.Equal(36_000m, position.MarketValue);
        Assert.Equal(36_000m, valuation.TotalMarketValue);
    }

    /// <summary>
    /// Verifies multiple instruments and closed positions are handled without emitting zero holdings.
    /// </summary>
    [Fact]
    public async Task ValuationService_MultipleInstruments_OmitsClosedPositions()
    {
        InMemoryStore store = CreateStoreWithParents();
        Guid closedInstrumentId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        Guid otherInstrumentId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        store.Instruments.Add(new Instrument(
            closedInstrumentId, "CLOSED", "Closed Synthetic Equity", "USD", InstrumentType.Equity));
        store.Instruments.Add(new Instrument(
            otherInstrumentId, "OTHER", "Other Synthetic Equity", "USD", InstrumentType.Equity));
        TradeApplicationService trades = CreateTradeService(store);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 3m, 10m, Start, CancellationToken.None);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, closedInstrumentId, TradeSide.Buy, 2m, 10m, Start, CancellationToken.None);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, closedInstrumentId, TradeSide.Sell, 2m, 11m, Start.AddMinutes(1),
            CancellationToken.None);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, otherInstrumentId, TradeSide.Buy, 4m, 10m, Start, CancellationToken.None);
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.NewGuid(), InstrumentId, 12.345m, "USD", Start),
            CancellationToken.None);
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.NewGuid(), otherInstrumentId, 5.25m, "USD", Start),
            CancellationToken.None);

        PortfolioValuation valuation = await CreateValuationService(store).GetPortfolioValuationAsync(
            PortfolioId,
            Start.AddMinutes(2),
            CancellationToken.None);

        Assert.Equal(2, valuation.Positions.Count);
        PortfolioValuationPosition firstPosition = valuation.Positions[0];
        PortfolioValuationPosition secondPosition = valuation.Positions[1];
        Assert.Equal(InstrumentId, firstPosition.InstrumentId);
        Assert.Equal(3m, firstPosition.Quantity);
        Assert.Equal(37.035m, firstPosition.MarketValue);
        Assert.Equal(otherInstrumentId, secondPosition.InstrumentId);
        Assert.Equal(4m, secondPosition.Quantity);
        Assert.Equal(21m, secondPosition.MarketValue);
        Assert.Equal(58.035m, valuation.TotalMarketValue);
    }

    /// <summary>
    /// Verifies historical valuation excludes trades and prices later than the cutoff.
    /// </summary>
    [Fact]
    public async Task ValuationService_HistoricalCutoff_UsesOnlyEligibleTradesAndPrices()
    {
        InMemoryStore store = CreateStoreWithParents();
        TradeApplicationService trades = CreateTradeService(store);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 100m, 10m, Start, CancellationToken.None);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Sell, 20m, 11m, Start.AddHours(2),
            CancellationToken.None);
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.NewGuid(), InstrumentId, 10m, "USD", Start),
            CancellationToken.None);
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.NewGuid(), InstrumentId, 20m, "USD", Start.AddHours(2)),
            CancellationToken.None);

        PortfolioValuation valuation = await CreateValuationService(store).GetPortfolioValuationAsync(
            PortfolioId,
            Start.AddHours(1),
            CancellationToken.None);

        PortfolioValuationPosition position = Assert.Single(valuation.Positions);
        Assert.Equal(100m, position.Quantity);
        Assert.Equal(10m, position.MarketPrice);
        Assert.Equal(Start, position.PriceAsOfUtc);
        Assert.Equal(1_000m, valuation.TotalMarketValue);
    }

    /// <summary>
    /// Verifies the greatest market-price identifier breaks equal-timestamp ties.
    /// </summary>
    [Fact]
    public async Task ValuationService_EqualPriceTimestamps_UsesGreatestPriceId()
    {
        InMemoryStore store = CreateStoreWithParents();
        await CreateTradeService(store).RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 2m, 10m, Start, CancellationToken.None);
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.Parse("30000000-0000-0000-0000-000000000001"), InstrumentId, 10m, "USD", Start),
            CancellationToken.None);
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.Parse("30000000-0000-0000-0000-000000000002"), InstrumentId, 12m, "USD", Start),
            CancellationToken.None);

        PortfolioValuation valuation = await CreateValuationService(store).GetPortfolioValuationAsync(
            PortfolioId,
            Start,
            CancellationToken.None);

        Assert.Equal(12m, Assert.Single(valuation.Positions).MarketPrice);
    }

    /// <summary>
    /// Verifies all instruments missing eligible prices are reported and no partial total is returned.
    /// </summary>
    [Fact]
    public async Task ValuationService_MultipleMissingPrices_ReportsEveryAffectedInstrument()
    {
        InMemoryStore store = CreateStoreWithParents();
        Guid secondInstrumentId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        store.Instruments.Add(new Instrument(
            secondInstrumentId, "OTHER", "Other Synthetic Equity", "USD", InstrumentType.Equity));
        TradeApplicationService trades = CreateTradeService(store);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 1m, 10m, Start, CancellationToken.None);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, secondInstrumentId, TradeSide.Buy, 1m, 10m, Start, CancellationToken.None);

        MissingMarketPricesException exception = await Assert.ThrowsAsync<MissingMarketPricesException>(
            () => CreateValuationService(store).GetPortfolioValuationAsync(
                PortfolioId,
                Start,
                CancellationToken.None));

        Assert.Equal(
            new[] { InstrumentId, secondInstrumentId }.OrderBy(id => id),
            exception.MissingInstruments.Select(instrument => instrument.InstrumentId).OrderBy(id => id));
        Assert.Equal(new[] { "OTHER", "TEST" }, exception.MissingInstruments.Select(item => item.Symbol).OrderBy(s => s));
    }

    /// <summary>
    /// Verifies a multiplication overflow is reported as a portfolio valuation overflow.
    /// </summary>
    [Fact]
    public async Task ValuationService_DecimalOverflow_ThrowsExplicitValuationException()
    {
        InMemoryStore store = CreateStoreWithParents();
        await CreateTradeService(store).RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, decimal.MaxValue, 1m, Start,
            CancellationToken.None);
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.NewGuid(), InstrumentId, decimal.MaxValue, "USD", Start),
            CancellationToken.None);

        PortfolioValuationOverflowException exception =
            await Assert.ThrowsAsync<PortfolioValuationOverflowException>(
                () => CreateValuationService(store).GetPortfolioValuationAsync(
                    PortfolioId,
                    Start,
                    CancellationToken.None));

        Assert.Equal(PortfolioId, exception.PortfolioId);
        Assert.IsType<OverflowException>(exception.InnerException);
    }

    /// <summary>
    /// Verifies checked summation rejects a portfolio total outside the decimal range.
    /// </summary>
    [Fact]
    public async Task ValuationService_TotalSumOverflow_ThrowsExplicitValuationException()
    {
        InMemoryStore store = CreateStoreWithParents();
        Guid secondInstrumentId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        store.Instruments.Add(new Instrument(
            secondInstrumentId, "OTHER", "Other Synthetic Equity", "USD", InstrumentType.Equity));
        TradeApplicationService trades = CreateTradeService(store);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, InstrumentId, TradeSide.Buy, 1m, 1m, Start, CancellationToken.None);
        await trades.RecordTradeAsync(
            Guid.NewGuid(), PortfolioId, secondInstrumentId, TradeSide.Buy, 1m, 1m, Start, CancellationToken.None);
        decimal largePrice = decimal.MaxValue * 0.75m;
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.NewGuid(), InstrumentId, largePrice, "USD", Start),
            CancellationToken.None);
        await ((IMarketPriceRepository)store).AddAsync(
            new MarketPrice(Guid.NewGuid(), secondInstrumentId, largePrice, "USD", Start),
            CancellationToken.None);

        await Assert.ThrowsAsync<PortfolioValuationOverflowException>(
            () => CreateValuationService(store).GetPortfolioValuationAsync(
                PortfolioId,
                Start,
                CancellationToken.None));
    }

    /// <summary>
    /// Verifies valuation cancellation is propagated to repository work.
    /// </summary>
    [Fact]
    public async Task ValuationService_CanceledToken_PropagatesCancellation()
    {
        InMemoryStore store = CreateStoreWithParents();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateValuationService(store).GetPortfolioValuationAsync(
                PortfolioId,
                Start,
                cancellation.Token));
    }

    private static PortfolioApplicationService CreatePortfolioService(InMemoryStore store, DateTimeOffset now)
    {
        return new PortfolioApplicationService(
            store,
            store,
            store,
            new PositionCalculationService(),
            new FixedTimeProvider(now));
    }

    private static TradeApplicationService CreateTradeService(InMemoryStore store)
    {
        return new TradeApplicationService(
            store,
            store,
            store,
            store,
            new PositionCalculationService());
    }

    private static PortfolioValuationApplicationService CreateValuationService(
        InMemoryStore store,
        TimeProvider? timeProvider = null)
    {
        return new PortfolioValuationApplicationService(
            store,
            store,
            store,
            store,
            store,
            new PositionCalculationService(),
            new StockValuationService(),
            timeProvider ?? new FixedTimeProvider(Start));
    }

    private static InMemoryStore CreateStoreWithParents()
    {
        InMemoryStore store = new();
        store.Portfolios.Add(new Portfolio(PortfolioId, "Test Portfolio", "USD", Start));
        store.Instruments.Add(new Instrument(
            InstrumentId,
            "TEST",
            "Synthetic Test Instrument",
            "USD",
            InstrumentType.Equity));
        return store;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public int CallCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            CallCount++;
            return now;
        }
    }

    private sealed class InMemoryStore :
        IPortfolioRepository,
        IInstrumentRepository,
        ITradeRepository,
        IMarketPriceRepository,
        IUnitOfWork
    {
        public List<Portfolio> Portfolios { get; } = [];
        public List<Instrument> Instruments { get; } = [];
        public List<Trade> Trades { get; } = [];
        public List<MarketPrice> MarketPrices { get; } = [];

        Task IPortfolioRepository.AddAsync(Portfolio portfolio, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Portfolios.Add(portfolio);
            return Task.CompletedTask;
        }

        Task<Portfolio?> IPortfolioRepository.GetByIdAsync(Guid portfolioId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Portfolios.SingleOrDefault(portfolio => portfolio.Id == portfolioId));
        }

        Task<IReadOnlyList<Portfolio>> IPortfolioRepository.GetAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Portfolio> result = Portfolios
                .OrderByDescending(portfolio => portfolio.CreatedAt)
                .ThenBy(portfolio => portfolio.Id)
                .ToArray();
            return Task.FromResult(result);
        }

        Task<IReadOnlyList<Trade>> ITradeRepository.GetByPortfolioIdAtOrBeforeAsync(
            Guid portfolioId,
            DateTimeOffset asOfUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Trade> result = OrderTrades(Trades.Where(
                trade => trade.PortfolioId == portfolioId && trade.ExecutedAt <= asOfUtc));
            return Task.FromResult(result);
        }

        Task IInstrumentRepository.AddAsync(Instrument instrument, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Instruments.Add(instrument);
            return Task.CompletedTask;
        }

        Task<Instrument?> IInstrumentRepository.GetByIdAsync(
            Guid instrumentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Instruments.SingleOrDefault(instrument => instrument.Id == instrumentId));
        }

        Task<Instrument?> IInstrumentRepository.GetBySymbolAsync(
            string symbol,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string normalizedSymbol = Instrument.NormalizeSymbol(symbol);
            return Task.FromResult(Instruments.SingleOrDefault(instrument => instrument.Symbol == normalizedSymbol));
        }

        Task<IReadOnlyList<Instrument>> IInstrumentRepository.GetByIdsAsync(
            IReadOnlyCollection<Guid> instrumentIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HashSet<Guid> requestedIds = instrumentIds.ToHashSet();
            IReadOnlyList<Instrument> result = Instruments
                .Where(instrument => requestedIds.Contains(instrument.Id))
                .ToArray();
            return Task.FromResult(result);
        }

        Task ITradeRepository.AddAsync(Trade trade, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Trades.Add(trade);
            return Task.CompletedTask;
        }

        Task<Trade?> ITradeRepository.GetByIdAsync(Guid tradeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Trades.SingleOrDefault(trade => trade.Id == tradeId));
        }

        Task<IReadOnlyList<Trade>> ITradeRepository.GetByPortfolioIdAsync(
            Guid portfolioId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Trade> result = OrderTrades(Trades.Where(trade => trade.PortfolioId == portfolioId));
            return Task.FromResult(result);
        }

        Task<IReadOnlyList<Trade>> ITradeRepository.GetByPortfolioAndInstrumentAsync(
            Guid portfolioId,
            Guid instrumentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Trade> result = OrderTrades(Trades.Where(
                trade => trade.PortfolioId == portfolioId && trade.InstrumentId == instrumentId));
            return Task.FromResult(result);
        }

        Task IMarketPriceRepository.AddAsync(MarketPrice marketPrice, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MarketPrices.Add(marketPrice);
            return Task.CompletedTask;
        }

        Task<MarketPrice?> IMarketPriceRepository.GetLatestByInstrumentIdAsync(
            Guid instrumentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MarketPrices
                .Where(marketPrice => marketPrice.InstrumentId == instrumentId)
                .OrderByDescending(marketPrice => marketPrice.AsOfUtc)
                .ThenByDescending(marketPrice => marketPrice.Id)
                .FirstOrDefault());
        }

        Task<IReadOnlyList<MarketPrice>> IMarketPriceRepository.GetLatestAtOrBeforeAsync(
            IReadOnlyCollection<Guid> instrumentIds,
            DateTimeOffset asOfUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HashSet<Guid> requestedIds = instrumentIds.ToHashSet();
            IReadOnlyList<MarketPrice> result = MarketPrices
                .Where(marketPrice =>
                    requestedIds.Contains(marketPrice.InstrumentId) &&
                    marketPrice.AsOfUtc <= asOfUtc)
                .GroupBy(marketPrice => marketPrice.InstrumentId)
                .Select(group => group
                    .OrderByDescending(marketPrice => marketPrice.AsOfUtc)
                    .ThenByDescending(marketPrice => marketPrice.Id)
                    .First())
                .ToArray();
            return Task.FromResult(result);
        }

        Task<MarketPrice?> IMarketPriceRepository.GetLatestAtOrBeforeAsync(
            Guid instrumentId,
            DateTimeOffset asOfUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MarketPrices
                .Where(marketPrice =>
                    marketPrice.InstrumentId == instrumentId &&
                    marketPrice.AsOfUtc <= asOfUtc)
                .OrderByDescending(marketPrice => marketPrice.AsOfUtc)
                .ThenByDescending(marketPrice => marketPrice.Id)
                .FirstOrDefault());
        }

        Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }

        Task<TResult> IUnitOfWork.ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(operation, cancellationToken);
        }

        Task<TResult> IUnitOfWork.ExecuteReadOnlyInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return operation(cancellationToken);
        }

        Task<TResult> IUnitOfWork.ExecuteWithExclusiveLocksAsync<TResult>(
            IReadOnlyCollection<string> lockResources,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ExecuteAsync(operation, cancellationToken);
        }

        private async Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TResult result = await operation(cancellationToken);
            await ((IUnitOfWork)this).SaveChangesAsync(cancellationToken);
            return result;
        }

        private static IReadOnlyList<Trade> OrderTrades(IEnumerable<Trade> trades)
        {
            return trades
                .OrderBy(trade => trade.ExecutedAt)
                .ThenBy(trade => trade.Side)
                .ThenBy(trade => trade.Id)
                .ToArray();
        }
    }
}
