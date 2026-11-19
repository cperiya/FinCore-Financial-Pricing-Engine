using FinCore.Application.Persistence;
using FinCore.Domain.Portfolios;
using FinCore.Domain.Valuation;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Orchestrates consistent, as-of portfolio valuation using Domain calculations and repository contracts.
/// </summary>
public sealed class PortfolioValuationApplicationService : IPortfolioValuationApplicationService
{
    private readonly IPortfolioRepository _portfolios;
    private readonly ITradeRepository _trades;
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketPriceRepository _marketPrices;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PositionCalculationService _positionCalculationService;
    private readonly StockValuationService _stockValuationService;
    private readonly TimeProvider _timeProvider;
    private readonly IPortfolioValuationDiagnostics? _diagnostics;

    /// <summary>
    /// Initializes portfolio valuation with repository, transaction, Domain-calculation, and clock dependencies.
    /// </summary>
    /// <param name="portfolios">Portfolio lookup operations.</param>
    /// <param name="trades">As-of trade-history queries.</param>
    /// <param name="instruments">Bulk instrument lookup operations.</param>
    /// <param name="marketPrices">Bulk latest-eligible-price queries.</param>
    /// <param name="unitOfWork">The serializable transaction boundary.</param>
    /// <param name="positionCalculationService">The Domain long-only position calculator.</param>
    /// <param name="stockValuationService">The Domain stock market-value calculator.</param>
    /// <param name="timeProvider">The UTC clock used when no cutoff is supplied.</param>
    /// <param name="diagnostics">An optional sink for benchmark-focused stage diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public PortfolioValuationApplicationService(
        IPortfolioRepository portfolios,
        ITradeRepository trades,
        IInstrumentRepository instruments,
        IMarketPriceRepository marketPrices,
        IUnitOfWork unitOfWork,
        PositionCalculationService positionCalculationService,
        StockValuationService stockValuationService,
        TimeProvider timeProvider,
        IPortfolioValuationDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(portfolios);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(marketPrices);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(positionCalculationService);
        ArgumentNullException.ThrowIfNull(stockValuationService);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _portfolios = portfolios;
        _trades = trades;
        _instruments = instruments;
        _marketPrices = marketPrices;
        _unitOfWork = unitOfWork;
        _positionCalculationService = positionCalculationService;
        _stockValuationService = stockValuationService;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public Task<PortfolioValuation> GetPortfolioValuationAsync(
        Guid portfolioId,
        DateTimeOffset? asOfUtc,
        CancellationToken cancellationToken)
    {
        DateTimeOffset effectiveCutoff = asOfUtc ?? _timeProvider.GetUtcNow();
        if (effectiveCutoff.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", nameof(asOfUtc));
        }

        return _unitOfWork.ExecuteReadOnlyInTransactionAsync(
            transactionCancellationToken =>
                CalculateValuationAsync(portfolioId, effectiveCutoff, transactionCancellationToken),
            cancellationToken);
    }

    private async Task<PortfolioValuation> CalculateValuationAsync(
        Guid portfolioId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        using IPortfolioValuationDiagnosticStage transactionStage =
            BeginStage("transaction.valuation", 0);
        using IPortfolioValuationDiagnosticStage portfolioStage =
            BeginStage("portfolio.lookup", 1);
        Portfolio? portfolio = await _portfolios.GetByIdAsync(portfolioId, cancellationToken);
        if (portfolio is null)
        {
            portfolioStage.Complete(0);
            throw new PortfolioNotFoundException(portfolioId);
        }

        portfolioStage.Complete(1);

        using IPortfolioValuationDiagnosticStage tradeStage =
            BeginStage("trade.retrieval-and-materialization", 1);
        IReadOnlyList<Trade> trades =
            await _trades.GetByPortfolioIdAtOrBeforeAsync(portfolioId, asOfUtc, cancellationToken);
        tradeStage.Complete(trades.Count);

        using IPortfolioValuationDiagnosticStage positionStage =
            BeginStage("position.calculation-grouping-sorting", trades.Count);
        IReadOnlyList<Position> positions = _positionCalculationService.CalculatePositions(trades);
        positionStage.Complete(positions.Count);
        if (positions.Count == 0)
        {
            PortfolioValuation emptyValuation = new(portfolio.Id, portfolio.BaseCurrency, asOfUtc, [], 0m);
            transactionStage.Complete(0);
            return emptyValuation;
        }

        using IPortfolioValuationDiagnosticStage instrumentInputStage =
            BeginStage("instrument-id-collection", positions.Count);
        Guid[] instrumentIds = positions.Select(position => position.InstrumentId).ToArray();
        instrumentInputStage.Complete(instrumentIds.Length);

        using IPortfolioValuationDiagnosticStage instrumentStage =
            BeginStage("instrument.retrieval-and-materialization", instrumentIds.Length);
        IReadOnlyList<Instrument> instruments =
            await _instruments.GetByIdsAsync(instrumentIds, cancellationToken);
        instrumentStage.Complete(instruments.Count);

        using IPortfolioValuationDiagnosticStage instrumentDictionaryStage =
            BeginStage("instrument-dictionary-and-validation", instruments.Count);
        Dictionary<Guid, Instrument> instrumentsById = instruments.ToDictionary(instrument => instrument.Id);
        foreach (Guid instrumentId in instrumentIds)
        {
            if (!instrumentsById.ContainsKey(instrumentId))
            {
                throw new InvalidOperationException(
                    $"Position instrument '{instrumentId}' does not exist in the instrument repository.");
            }
        }
        instrumentDictionaryStage.Complete(instrumentsById.Count);

        using IPortfolioValuationDiagnosticStage priceStage =
            BeginStage("market-price.retrieval-and-materialization", instrumentIds.Length);
        IReadOnlyList<MarketPrice> marketPrices =
            await _marketPrices.GetLatestAtOrBeforeAsync(instrumentIds, asOfUtc, cancellationToken);
        priceStage.Complete(marketPrices.Count);

        using IPortfolioValuationDiagnosticStage valuationPreparationStage =
            BeginStage("price-dictionary-and-missing-price-validation", marketPrices.Count);
        Dictionary<Guid, MarketPrice> pricesByInstrumentId =
            marketPrices.ToDictionary(marketPrice => marketPrice.InstrumentId);

        MissingMarketPriceInstrument[] missingInstruments = positions
            .Where(position => !pricesByInstrumentId.ContainsKey(position.InstrumentId))
            .Select(position =>
            {
                Instrument instrument = instrumentsById[position.InstrumentId];
                return new MissingMarketPriceInstrument(instrument.Id, instrument.Symbol);
            })
            .ToArray();
        if (missingInstruments.Length > 0)
        {
            throw new MissingMarketPricesException(portfolioId, asOfUtc, missingInstruments);
        }
        valuationPreparationStage.Complete(pricesByInstrumentId.Count);

        try
        {
            using IPortfolioValuationDiagnosticStage responseStage =
                BeginStage("valuation-response-and-checked-decimal-summation", positions.Count);
            decimal totalMarketValue = 0m;
            List<PortfolioValuationPosition> valuedPositions = new(positions.Count);
            foreach (Position position in positions)
            {
                Instrument instrument = instrumentsById[position.InstrumentId];
                MarketPrice marketPrice = pricesByInstrumentId[position.InstrumentId];
                decimal marketValue = _stockValuationService.CalculateMarketValue(
                    marketPrice.Price,
                    position.Quantity);
                totalMarketValue = checked(totalMarketValue + marketValue);
                valuedPositions.Add(new PortfolioValuationPosition(
                    instrument.Id,
                    instrument.Symbol,
                    position.Quantity,
                    marketPrice.Price,
                    marketPrice.AsOfUtc,
                    marketValue));
            }

            PortfolioValuation valuation = new(
                portfolio.Id,
                portfolio.BaseCurrency,
                asOfUtc,
                valuedPositions,
                totalMarketValue);
            responseStage.Complete(valuedPositions.Count);
            transactionStage.Complete(valuedPositions.Count);
            return valuation;
        }
        catch (OverflowException exception)
        {
            throw new PortfolioValuationOverflowException(portfolioId, asOfUtc, exception);
        }
    }

    private IPortfolioValuationDiagnosticStage BeginStage(string name, int inputCount)
    {
        return _diagnostics?.BeginStage(name, inputCount) ?? NoOpPortfolioValuationDiagnosticStage.Instance;
    }

    private sealed class NoOpPortfolioValuationDiagnosticStage : IPortfolioValuationDiagnosticStage
    {
        public static NoOpPortfolioValuationDiagnosticStage Instance { get; } = new();

        public void Complete(int outputCount)
        {
        }

        public void Dispose()
        {
        }
    }
}
