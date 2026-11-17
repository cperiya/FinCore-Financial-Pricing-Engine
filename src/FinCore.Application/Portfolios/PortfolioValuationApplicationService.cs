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
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public PortfolioValuationApplicationService(
        IPortfolioRepository portfolios,
        ITradeRepository trades,
        IInstrumentRepository instruments,
        IMarketPriceRepository marketPrices,
        IUnitOfWork unitOfWork,
        PositionCalculationService positionCalculationService,
        StockValuationService stockValuationService,
        TimeProvider timeProvider)
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
        Portfolio? portfolio = await _portfolios.GetByIdAsync(portfolioId, cancellationToken);
        if (portfolio is null)
        {
            throw new PortfolioNotFoundException(portfolioId);
        }

        IReadOnlyList<Trade> trades =
            await _trades.GetByPortfolioIdAtOrBeforeAsync(portfolioId, asOfUtc, cancellationToken);
        IReadOnlyList<Position> positions = _positionCalculationService.CalculatePositions(trades);
        if (positions.Count == 0)
        {
            return new PortfolioValuation(portfolio.Id, portfolio.BaseCurrency, asOfUtc, [], 0m);
        }

        Guid[] instrumentIds = positions.Select(position => position.InstrumentId).ToArray();
        IReadOnlyList<Instrument> instruments =
            await _instruments.GetByIdsAsync(instrumentIds, cancellationToken);
        Dictionary<Guid, Instrument> instrumentsById = instruments.ToDictionary(instrument => instrument.Id);
        foreach (Guid instrumentId in instrumentIds)
        {
            if (!instrumentsById.ContainsKey(instrumentId))
            {
                throw new InvalidOperationException(
                    $"Position instrument '{instrumentId}' does not exist in the instrument repository.");
            }
        }

        IReadOnlyList<MarketPrice> marketPrices =
            await _marketPrices.GetLatestAtOrBeforeAsync(instrumentIds, asOfUtc, cancellationToken);
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

        try
        {
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

            return new PortfolioValuation(
                portfolio.Id,
                portfolio.BaseCurrency,
                asOfUtc,
                valuedPositions,
                totalMarketValue);
        }
        catch (OverflowException exception)
        {
            throw new PortfolioValuationOverflowException(portfolioId, asOfUtc, exception);
        }
    }
}
