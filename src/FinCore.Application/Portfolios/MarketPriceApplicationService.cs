using FinCore.Application.Persistence;
using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Implements simulated USD market-price recording and as-of retrieval.
/// </summary>
public sealed class MarketPriceApplicationService : IMarketPriceApplicationService
{
    private readonly IInstrumentRepository _instruments;
    private readonly IMarketPriceRepository _marketPrices;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes market-price use cases with instrument, price, and commit operations.
    /// </summary>
    /// <param name="instruments">Instrument lookup operations.</param>
    /// <param name="marketPrices">Market-price persistence operations.</param>
    /// <param name="unitOfWork">The persistence commit boundary.</param>
    public MarketPriceApplicationService(
        IInstrumentRepository instruments,
        IMarketPriceRepository marketPrices,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(marketPrices);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _instruments = instruments;
        _marketPrices = marketPrices;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<MarketPrice> RecordPriceAsync(
        Guid instrumentId,
        decimal price,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (await _instruments.GetByIdAsync(instrumentId, cancellationToken) is null)
        {
            throw new InstrumentNotFoundException(instrumentId);
        }

        MarketPrice marketPrice = new(Guid.NewGuid(), instrumentId, price, "USD", asOfUtc);
        await _marketPrices.AddAsync(marketPrice, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return marketPrice;
    }

    /// <inheritdoc />
    public async Task<MarketPrice?> GetLatestEligiblePriceAsync(
        Guid instrumentId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (asOfUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", nameof(asOfUtc));
        }

        if (await _instruments.GetByIdAsync(instrumentId, cancellationToken) is null)
        {
            throw new InstrumentNotFoundException(instrumentId);
        }

        return await _marketPrices.GetLatestAtOrBeforeAsync(
            instrumentId,
            asOfUtc,
            cancellationToken);
    }
}
