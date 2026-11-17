using FinCore.Application.Persistence;
using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Implements idempotent trade recording with complete-history position validation.
/// </summary>
public sealed class TradeApplicationService : ITradeApplicationService
{
    private readonly IPortfolioRepository _portfolios;
    private readonly IInstrumentRepository _instruments;
    private readonly ITradeRepository _trades;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PositionCalculationService _positionCalculationService;

    /// <summary>
    /// Initializes trade processing with repositories, a transaction boundary, and the Domain calculator.
    /// </summary>
    /// <param name="portfolios">Portfolio lookup operations.</param>
    /// <param name="instruments">Instrument lookup operations.</param>
    /// <param name="trades">Trade-history persistence operations.</param>
    /// <param name="unitOfWork">The SQL transaction boundary and lock coordinator.</param>
    /// <param name="positionCalculationService">The Domain position calculator.</param>
    public TradeApplicationService(
        IPortfolioRepository portfolios,
        IInstrumentRepository instruments,
        ITradeRepository trades,
        IUnitOfWork unitOfWork,
        PositionCalculationService positionCalculationService)
    {
        ArgumentNullException.ThrowIfNull(portfolios);
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(positionCalculationService);

        _portfolios = portfolios;
        _instruments = instruments;
        _trades = trades;
        _unitOfWork = unitOfWork;
        _positionCalculationService = positionCalculationService;
    }

    /// <inheritdoc />
    public Task<Trade> RecordTradeAsync(
        Guid tradeId,
        Guid portfolioId,
        Guid instrumentId,
        TradeSide side,
        decimal quantity,
        decimal unitPrice,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken)
    {
        Trade candidate = new(
            tradeId,
            portfolioId,
            instrumentId,
            side,
            quantity,
            unitPrice,
            executedAt);
        string[] lockResources =
        [
            $"FinCore:Trade:{tradeId:N}",
            $"FinCore:Position:{portfolioId:N}:{instrumentId:N}"
        ];

        return _unitOfWork.ExecuteWithExclusiveLocksAsync(
            lockResources,
            async transactionToken =>
            {
                Trade? existingTrade = await _trades.GetByIdAsync(tradeId, transactionToken);
                if (existingTrade is not null)
                {
                    if (HasSameCanonicalPayload(existingTrade, candidate))
                    {
                        return existingTrade;
                    }

                    throw new TradeIdempotencyConflictException(tradeId);
                }

                if (await _portfolios.GetByIdAsync(portfolioId, transactionToken) is null)
                {
                    throw new PortfolioNotFoundException(portfolioId);
                }

                if (await _instruments.GetByIdAsync(instrumentId, transactionToken) is null)
                {
                    throw new InstrumentNotFoundException(instrumentId);
                }

                IReadOnlyList<Trade> history =
                    await _trades.GetByPortfolioAndInstrumentAsync(
                        portfolioId,
                        instrumentId,
                        transactionToken);

                _positionCalculationService.CalculatePositions(history.Append(candidate));
                await _trades.AddAsync(candidate, transactionToken);
                return candidate;
            },
            cancellationToken);
    }

    private static bool HasSameCanonicalPayload(Trade stored, Trade requested)
    {
        return stored.Id == requested.Id &&
            stored.PortfolioId == requested.PortfolioId &&
            stored.InstrumentId == requested.InstrumentId &&
            stored.Side == requested.Side &&
            stored.Quantity == requested.Quantity &&
            stored.UnitPrice == requested.UnitPrice &&
            stored.ExecutedAt == requested.ExecutedAt;
    }
}
